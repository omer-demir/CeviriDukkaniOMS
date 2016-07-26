namespace OMS.Api {
    public class Settings {
        public string RabbitHost { get; set; }
        public int RabbitPort { get; set; }
        public string RabbitUserName { get; set; }
        public string RabbitPassword { get; set; }
        public string RabbitExchangeName { get; set; }
        public string DocumentServiceEndpoint { get; set; }
    }
}