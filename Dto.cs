namespace SpeedyCdn.Dto
{
    public class UrlGenerationDto
    {
        public bool HasSignature { get; set; }
    }

    public class DisplayUrlDto
    {
        public string Display { get; set; }

        public string RedirectPath { get; set; }

        public string QueryString { get; set; }
    }

    public class UuidUrlDto
    {
        public string Uuid { get; set; }

        public string RedirectPath { get; set; }

        public string QueryString { get; set; }
    }
}
