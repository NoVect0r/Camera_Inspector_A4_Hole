using System.Collections.Generic;

namespace Camera_Insepctor_Project.Models
{
    internal class InspectionSettings
    {
        public int CameraIndex { get; set; } = 0;
        public int RequestedCameraWidth { get; set; } = 1280;
        public int RequestedCameraHeight { get; set; } = 720;
        public double ExpectedLongMm { get; set; } = 297.0;
        public double ExpectedShortMm { get; set; } = 210.0;
        public double ToleranceMm { get; set; } = 0.5;
        public double UndistortAlpha { get; set; } = 0.1;
        public string IntrinsicCalibrationPath { get; set; } = "Calibration/camera_intrinsic.json";
        public double ExpectedHoleDiameterMm { get; set; } = 6.0;
        public double HolePositionToleranceMm { get; set; } = 1.0;
        public List<HoleReference> HoleReferences { get; set; } = new();
    }
}
