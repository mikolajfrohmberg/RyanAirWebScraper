using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using System.Globalization;

namespace RyanAirScraper
{
    class Program
    {
        
        // Function that returns HttpWebResponse for given URL
        static String returnResponse(string Url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Url);
            request.ContentType = "application/json; charset=utf-8";
            HttpWebResponse response = request.GetResponse() as HttpWebResponse;
            using (Stream responseStream = response.GetResponseStream())
            {
                StreamReader reader = new StreamReader(responseStream, Encoding.UTF8);
                string responseText = reader.ReadToEnd();
                response.Close();
                return responseText;
            }
        }

        // Function that extracts IATA codes of airports connected with chosen airport
        // Airport IATACode -> IATA code of chosen airport
        static List<String> connectionsFromAirport(String airportIATACode)
        {
            List<String> connectionsFound = new List<String>();
            string Url = "https://www.ryanair.com/api/locate/4/common?embedded=airports,routes&market=en-us";

            // Sending request and recieving server response
            string responseText = returnResponse(Url);
            dynamic airportsJson = JsonConvert.DeserializeObject(responseText);

            int currIndex = 0;
            dynamic selectedAirport = null;
            foreach (var airport in airportsJson.airports)
            {
                if (airport.iataCode == airportIATACode) // If given AirportIATACode has been found in response...
                {
                    selectedAirport = airport;
                    //Console.WriteLine("Found at index: {0}", currIndex);
                    break;
                }
                currIndex++;
            }

            if (selectedAirport == null) // There were no records found for given AirportIATACode
            {
                Console.WriteLine("No records found!\n");
                return connectionsFound;
            }

            foreach (var destination in selectedAirport.routes)
            {
                Regex airportRegex = new Regex(@"airport:(?<IATACode>[A-Z]{3})");
                Match match = airportRegex.Match(destination.ToString());
                if (match.Success) // if record is IATACode of Airport (matches regex)...
                {
                    // Add IATACode of airport to collection
                    connectionsFound.Add(match.Groups["IATACode"].Value);
                }
            }

            return connectionsFound;
        }


        static List<Record> findFlightsFromAirport(string airportIATACode, string startDate, string endDate)
        {
            List<Record> flightsFound = new List<Record>();
            List<String> connectionsFound = connectionsFromAirport(airportIATACode);

            int airportCounter = 0; // Counts number of airports that were already checked
            
            foreach(String arrivalIataCode in connectionsFound)
            {
                Console.Clear();
                Console.WriteLine("Fetched {0} out of {1} possible airports", airportCounter, connectionsFound.Count);
                flightsFound.AddRange(findFlightsForConnection(airportIATACode, arrivalIataCode, startDate, endDate));
                airportCounter++;
            }

            Console.Clear();
            Console.WriteLine("Fetched all data!");

            return flightsFound;
        }

        // Function, that searches for flights from departure airport to arrival airport in given dates
        static List<Record> findFlightsForConnection(string departureIata, string arrivalIata, string searchDateBegin, string searchDateEnd)
        {
            DateTime beginDate = DateTime.ParseExact(searchDateBegin, "yyyy-MM-dd",CultureInfo.InvariantCulture);
            DateTime endDate = DateTime.ParseExact(searchDateEnd, "yyyy-MM-dd", CultureInfo.InvariantCulture);

            List<Record> flightsFound = new List<Record>();

            // Calculate number of days between 2 dates (excluding begin date)
            int daysDifference = endDate.Subtract(beginDate).Days;

            // Date value of dateOut parameter
            DateTime dateOut = beginDate;

            // TO DO - Change way of indexing flights
            int flightIndex = 1;

            // For loop used to divide searches, which can be done only for up to week forward
            for (int days = 0; days <= daysDifference;)
            {
                int daysSearchedForward = (daysDifference - days > 6) ? 6 : (daysDifference - days);

                // Currency of returned prices depend on localization chosen in following link
                // e.g "pl-pl" -> Polish Złoty, "en-us" -> American Dollars
                string Url = "https://www.ryanair.com/api/booking/v4/pl-pl/availability?ToUs=AGREED&DateOut=";
                Url += dateOut.ToString("yyyy-MM-dd");
                Url += "&Origin=" + departureIata;
                Url += "&Destination=" + arrivalIata;
                Url += "&FlexDaysOut=" + daysSearchedForward;
                Url += "&CHD=1&TEEN=1&ADT=1"; // Needed to get prices for each age group

                // Sending request and recieving server response
                string responseText = returnResponse(Url);

                // Extracting information about available flights for recieved data
                dynamic availableJson = JsonConvert.DeserializeObject(responseText);
                var availableDates = availableJson.trips.First.dates;

                // Checking information about available (if actually available) flights for each day from response
                foreach(var checkedDateInfo in availableDates)
                {
                    // If no flights available for given day, then skip that date
                    if (checkedDateInfo.flights.Count == 0)
                        continue;

                    // Saving catched information into Record object and pushing it to table
                    foreach(var flightInfo in checkedDateInfo.flights)
                    {
                        Record informationAboutFlight = new Record();
                        informationAboutFlight.airportDepartureCode = availableJson.trips.First.origin;
                        informationAboutFlight.airportArrivalCode = availableJson.trips.First.destination;
                        informationAboutFlight.departureName = availableJson.trips.First.originName;
                        informationAboutFlight.arrivalName = availableJson.trips.First.destinationName;
                        informationAboutFlight.departureDate = flightInfo.time[0];
                        informationAboutFlight.arrivalDate = flightInfo.time[1];
                        informationAboutFlight.flightDuration = flightInfo.duration;
                        informationAboutFlight.flightNumber = flightInfo.flightNumber;

                        // Extracting fares for children (2-11yrs), teens (12-15yrs) and adults
                        foreach(var fareInfo in flightInfo.regularFare.fares)
                        {
                            if(fareInfo.type == "ADT")
                            {
                                informationAboutFlight.adultPrice = fareInfo.publishedFare;
                                informationAboutFlight.adultAfterDiscount = fareInfo.amount;
                            }
                            else if(fareInfo.type == "TEEN")
                            {
                                informationAboutFlight.teenPrice = fareInfo.publishedFare;
                                informationAboutFlight.teenAfterDiscount = fareInfo.amount;
                            }
                            else if(fareInfo.type == "CHD")
                            {
                                informationAboutFlight.childPrice = fareInfo.publishedFare;
                                informationAboutFlight.childAfterDiscount = fareInfo.amount;
                            }
                        }
                        flightsFound.Add(informationAboutFlight);
                        flightIndex++;
                    }
                }
                //Console.WriteLine("Search done for dateOut: {0} and {1} days.", dateOut, daysSearchedForward + 1);
                dateOut = dateOut.AddDays(daysSearchedForward + 1);
                days += (daysSearchedForward+1);
            }


            return flightsFound;
        }

        // Function that prints all IATA codes of airports connected with searched airport
        static void printFoundConnections(List<String> connections)
        {
            for (int i = 0; i < connections.Count; i++)
            {
                Console.WriteLine("{0}. {1}", i + 1, connections[i]);
            }
        }

        // Function that prints all needed information about found flights (with available discounts)
        static void ShowInformationAboutFlights(List<Record> flights, string currencySymbol)
        {
            int indexOfFlight = 1;
            foreach(Record flightInfo in flights)
            {
                Console.WriteLine("-----------------------------------------");
                Console.WriteLine("{0}. Flight number: {1}", indexOfFlight, flightInfo.flightNumber);
                Console.WriteLine("{0} ({1}) -> {2} ({3})", flightInfo.departureName, flightInfo.airportDepartureCode,
                    flightInfo.arrivalName, flightInfo.airportArrivalCode);
                Console.WriteLine("Departure date: {0}", flightInfo.departureDate);
                Console.WriteLine("Arrival date: {0}", flightInfo.arrivalDate);
                Console.WriteLine("Flight duration: {0}", flightInfo.flightDuration);
                Console.WriteLine("Ticket prices:");
                Console.Write("Adults: {0}{1}", flightInfo.adultPrice, currencySymbol);
                if(flightInfo.adultAfterDiscount < flightInfo.adultPrice)
                    Console.Write(" / After discount: {0}{1}", flightInfo.adultAfterDiscount, currencySymbol);
                Console.Write("\nTeens: {0}{1}", flightInfo.teenPrice, currencySymbol);
                if (flightInfo.teenAfterDiscount < flightInfo.teenPrice)
                    Console.Write(" / After discount: {0}{1}", flightInfo.teenAfterDiscount, currencySymbol);
                Console.Write("\nChildren: {0}{1}", flightInfo.childPrice, currencySymbol);
                if (flightInfo.childAfterDiscount < flightInfo.childPrice)
                    Console.Write(" / After discount: {0}{1}", flightInfo.childAfterDiscount, currencySymbol);
                Console.WriteLine();
                indexOfFlight++;
            }
        }

        static void Main(string[] args)
        {
            // e.g - Searching for connections from Poznań, Poland -> IATA "POZ"
            //List<String> connectionsFromPOZ = connectionsFromAirport("POZ");
            //printFoundConnections(connectionsFromPOZ);

            // e.g - Searching for connections from Poznań, Poland -> IATA "POZ" to Athenes, Greece -> IATA "ATH"
            // in given dates -> from 25th june 2020 to 3rd july 2020
            //List <Record> flightsList = findFlightsForConnection("POZ", "ATH", "2020-06-25", "2020-07-03");

            // e.g - Searching for any possible connections from Poznań, Poland -> IATA "POZ"
            // in given dates -> from 25th june 2020 to 28th june 2020
            List<Record> flightsListFromPOZ = findFlightsFromAirport("POZ", "2020-06-25", "2020-06-28");
            ShowInformationAboutFlights(flightsListFromPOZ, "zł");
        }
    }
}
