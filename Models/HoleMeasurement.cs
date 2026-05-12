namespace Camera_Insepctor_Project.Models
{
    internal class HoleMeasurement
    {
        public string CornerName { get; set; } = string.Empty;
        public bool IsDetected { get; set; }
        public double ExpectedCenterXmm { get; set; }
        public double ExpectedCenterYmm { get; set; }
        public double CenterXmm { get; set; }
        public double CenterYmm { get; set; }
        public double PositionErrorMm { get; set; }
        public double DiameterMm { get; set; }
        public double DiameterErrorMm { get; set; }
        public double Circularity { get; set; }
        public int CandidateCount { get; set; }
        public int ExtraHoleCount { get; set; }
        public bool IsInTolerance { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
