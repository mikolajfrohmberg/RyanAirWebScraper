namespace RyanAirScraper
{
    struct Record
    {
        public string airportDepartureCode, airportArrivalCode;
        public string departureName, arrivalName;
        public string departureDate, arrivalDate;
        public double childPrice, teenPrice, adultPrice;
        public double childAfterDiscount, teenAfterDiscount, adultAfterDiscount;
        public string flightNumber;
        public string flightDuration;
    }
}