namespace Camera_Insepctor_Project.Models
{
    internal class HoleReference
    {
        public string CornerName { get; set; } = string.Empty;
        public double CenterXmm { get; set; }
        public double CenterYmm { get; set; }
        public double DiameterMm { get; set; }
        public double Circularity { get; set; }
    }
}
