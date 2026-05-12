using System;
using System.Collections.Generic;
using System.Text;

namespace Camera_Insepctor_Project.Models
{
    // State는 InspectionState enum으로 정의되어 있음
    // MeasuredValue는 실제 측정값
    // IsInTolerance는 측정값이 허용 범위 내에 있는지 여부
    // Timestamp는 검사 결과가 기록된 시간

    /*
     검사를 1 cycle 돌렸을 때의 결과를 담기 위한 class
     */

    internal class InspectionResult
    {
        public InspectionState State { get; set; }

        // 기존 UI 호환용 대표 측정값
        // 현재는 긴 면 측정값으로 사용
        public double MeasuredValue { get; set; }

        // 새로 추가: 긴 면 / 짧은 면 측정값
        public double MeasuredLongMm { get; set; }
        public double MeasuredShortMm { get; set; }

        // 디버깅용 pixel 값
        public double MeasuredLongPx { get; set; }
        public double MeasuredShortPx { get; set; }

        public bool IsA4SheetDetected { get; set; }
        public int A4CanonicalWidthPx { get; set; }
        public int A4CanonicalHeightPx { get; set; }
        public double CornerRoiSizeMm { get; set; }
        public double ExpectedHoleDiameterMm { get; set; }
        public List<HoleMeasurement> HoleMeasurements { get; set; } = new();
        public string DetailMessage { get; set; } = string.Empty;

        public bool IsInTolerance { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
