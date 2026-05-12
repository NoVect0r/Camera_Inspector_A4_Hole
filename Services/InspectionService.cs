using Camera_Insepctor_Project.Models;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

/* 실제 검사 엔진 구현

카메라 프레임 받기
ROI 설정
이미지 전처리
contour 탐색
best contour 선정
Bounding Rect를 이용한 pixel 계산
pixel to mm 변환

*/

namespace Camera_Insepctor_Project.Services
{
    internal class InspectionService
    {
        private const double A4AspectRatio = 297.0 / 210.0;
        private const double A4AspectRatioTolerance = 0.35;
        private const double MinA4AreaRatio = 0.03;
        private const double MaxA4AreaRatio = 0.9;
        private const double MinA4Extent = 0.55;
        private const int A4MaxSaturation = 80;
        private const int A4MinValue = 120;
        private const double CornerHoleRoiSizeMm = 45.0;
        private const double ExpectedHoleDiameterMm = 6.0;
        private const double MinHoleCircularity = 0.55;

        public InspectionResult Inspect(
            Mat frame,
            double mmPerPixel,
            double expectedLongMm,
            double expectedShortMm,
            double toleranceMm)
        {
            try
            {
                if (frame == null || frame.Empty())
                {
                    return new InspectionResult
                    {
                        State = InspectionState.Error,
                        MeasuredValue = 0,
                        IsInTolerance = false,
                        Timestamp = DateTime.Now
                    };
                }

                if (mmPerPixel <= 0)
                {
                    return new InspectionResult
                    {
                        State = InspectionState.Error,
                        MeasuredValue = 0,
                        IsInTolerance = false,
                        Timestamp = DateTime.Now
                    };
                }

                if (expectedLongMm <= 0 ||
                    expectedShortMm <= 0 ||
                    toleranceMm < 0)
                {
                    return new InspectionResult
                    {
                        State = InspectionState.Error,
                        MeasuredValue = 0,
                        IsInTolerance = false,
                        Timestamp = DateTime.Now
                    };
                }

                if (!TryFindTargetRect(frame, out Rect targetRect))
                {
                    return new InspectionResult
                    {
                        State = InspectionState.NoObject,
                        MeasuredValue = 0,
                        IsInTolerance = false,
                        Timestamp = DateTime.Now
                    };
                }

                // 디버깅용 overlay
                DrawDebugOverlay(frame, targetRect);

                // 가로/세로가 아니라 긴 면 / 짧은 면 기준으로 측정
                double longSidePx = Math.Max(targetRect.Width, targetRect.Height);
                double shortSidePx = Math.Min(targetRect.Width, targetRect.Height);

                // 보정된 corrected frame 기준에서는
                // reference pixel 값을 다시 계산하지 않고,
                // CalibrationData.MmPerPixel에서 전달받은 값을 그대로 사용한다.
                if (!TryConvertPixelSizeToMillimeter(longSidePx, mmPerPixel, out double measuredLongMm))
                {
                    return new InspectionResult
                    {
                        State = InspectionState.Error,
                        MeasuredValue = 0,
                        IsInTolerance = false,
                        Timestamp = DateTime.Now
                    };
                }

                if (!TryConvertPixelSizeToMillimeter(shortSidePx, mmPerPixel, out double measuredShortMm))
                {
                    return new InspectionResult
                    {
                        State = InspectionState.Error,
                        MeasuredValue = 0,
                        IsInTolerance = false,
                        Timestamp = DateTime.Now
                    };
                }

                double minLongMm = expectedLongMm - toleranceMm;
                double maxLongMm = expectedLongMm + toleranceMm;

                double minShortMm = expectedShortMm - toleranceMm;
                double maxShortMm = expectedShortMm + toleranceMm;

                bool isLongInTolerance = IsWithinTolerance(measuredLongMm, minLongMm, maxLongMm);
                bool isShortInTolerance = IsWithinTolerance(measuredShortMm, minShortMm, maxShortMm);

                bool isInTolerance = isLongInTolerance && isShortInTolerance;

                return new InspectionResult
                {
                    State = isInTolerance ? InspectionState.Ok : InspectionState.Warning,

                    // 기존 대표값은 긴 면으로 유지
                    MeasuredValue = measuredLongMm,

                    // 새로 추가한 상세 측정값
                    MeasuredLongMm = measuredLongMm,
                    MeasuredShortMm = measuredShortMm,
                    MeasuredLongPx = longSidePx,
                    MeasuredShortPx = shortSidePx,

                    IsInTolerance = isInTolerance,
                    Timestamp = DateTime.Now
                };
            }
            catch
            {
                return new InspectionResult
                {
                    State = InspectionState.Error,
                    MeasuredValue = 0,
                    IsInTolerance = false,
                    Timestamp = DateTime.Now
                };
            }
        }

        private bool TryFindTargetRect(Mat frame, out Rect targetRect)
        {
            targetRect = new Rect();

            // TODO:
            // ROI 적용
            // grayscale
            // threshold
            // morphology
            // contour detection
            // best contour selection
            // BoundingRect 계산

            if (frame == null || frame.Empty())
            {
                return false;
            }

            using var gray = new Mat();
            using var binary = new Mat();
            using var morph = new Mat();

            Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);

            Cv2.Threshold(gray, binary, 100, 255, ThresholdTypes.Binary);

            var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
            Cv2.MorphologyEx(binary, morph, MorphTypes.Close, kernel);

            Cv2.FindContours(
                morph,
                out Point[][] contours,
                out HierarchyIndex[] _,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

            if (contours == null || contours.Length == 0)
            {
                return false;
            }

            double maxArea = 0;
            int bestContourIndex = -1;

            for (int i = 0; i < contours.Length; i++)
            {
                double area = Cv2.ContourArea(contours[i]);

                if (area > maxArea)
                {
                    maxArea = area;
                    bestContourIndex = i;
                }
            }

            if (bestContourIndex < 0)
            {
                return false;
            }

            targetRect = Cv2.BoundingRect(contours[bestContourIndex]);

            if (targetRect.Width <= 0 || targetRect.Height <= 0)
            {
                targetRect = new Rect();
                return false;
            }

            return true;
        }

        // 새 Homography 기반 측정 루트에서 사용할 객체 검출 메서드.
        // 기존 BoundingRect가 아니라 RotatedRect를 반환한다.
        // frame은 undistorted full frame이 들어오는 것을 전제로 한다.
        private bool TryFindTargetRotatedRect(
            Mat frame,
            Rect? inspectionRoi,
            out RotatedRect targetRect)
        {
            targetRect = default;

            if (frame == null || frame.Empty())
            {
                return false;
            }

            Rect roi = GetSafeInspectionRoi(frame, inspectionRoi);

            if (roi.Width <= 0 || roi.Height <= 0)
            {
                return false;
            }

            using var roiFrame = new Mat(frame, roi);
            using Mat morph = CreateA4WhiteMask(roiFrame);

            Cv2.FindContours(
                morph,
                out Point[][] contours,
                out HierarchyIndex[] _,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

            if (contours == null || contours.Length == 0)
            {
                return false;
            }

            if (!TrySelectBestA4Contour(
                contours,
                roi,
                out Point[]? selectedContour))
            {
                return false;
            }

            Point[] globalContour = selectedContour!
                .Select(point => new Point(point.X + roi.X, point.Y + roi.Y))
                .ToArray();

            targetRect = Cv2.MinAreaRect(globalContour);

            if (targetRect.Size.Width <= 0 || targetRect.Size.Height <= 0)
            {
                targetRect = default;
                return false;
            }

            return true;
        }

        private bool TryDetectA4SheetCorners(
            Mat frame,
            Rect? inspectionRoi,
            out Point2f[] orderedCorners)
        {
            orderedCorners = Array.Empty<Point2f>();

            if (frame == null || frame.Empty())
            {
                return false;
            }

            Rect roi = GetSafeInspectionRoi(frame, inspectionRoi);

            if (roi.Width <= 0 || roi.Height <= 0)
            {
                return false;
            }

            using var roiFrame = new Mat(frame, roi);
            using Mat morph = CreateA4WhiteMask(roiFrame);

            Cv2.FindContours(
                morph,
                out Point[][] contours,
                out HierarchyIndex[] _,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

            if (contours == null || contours.Length == 0)
            {
                return false;
            }

            if (!TrySelectBestA4Contour(
                contours,
                roi,
                out Point[]? selectedContour) ||
                selectedContour == null)
            {
                return false;
            }

            Point[] globalContour = selectedContour
                .Select(point => new Point(point.X + roi.X, point.Y + roi.Y))
                .ToArray();

            Point2f[] corners = TryApproximateQuadrilateralCorners(
                globalContour,
                out Point2f[] approximatedCorners)
                    ? approximatedCorners
                    : Cv2.MinAreaRect(globalContour).Points();

            if (corners.Length != 4)
            {
                return false;
            }

            orderedCorners = OrderA4CornersForCanonicalView(corners);
            return true;
        }

        private Mat CreateA4WhiteMask(Mat roiFrame)
        {
            if (roiFrame == null || roiFrame.Empty())
            {
                throw new ArgumentException("ROI frame is empty.", nameof(roiFrame));
            }

            using var hsv = new Mat();
            using var mask = new Mat();
            using var closed = new Mat();

            Cv2.CvtColor(roiFrame, hsv, ColorConversionCodes.BGR2HSV);

            Cv2.InRange(
                hsv,
                new Scalar(0, 0, A4MinValue),
                new Scalar(179, A4MaxSaturation, 255),
                mask);

            using Mat closeKernel = Cv2.GetStructuringElement(
                MorphShapes.Rect,
                new Size(15, 15));

            Cv2.MorphologyEx(
                mask,
                closed,
                MorphTypes.Close,
                closeKernel,
                iterations: 2);

            using Mat openKernel = Cv2.GetStructuringElement(
                MorphShapes.Rect,
                new Size(5, 5));

            var result = new Mat();

            Cv2.MorphologyEx(
                closed,
                result,
                MorphTypes.Open,
                openKernel,
                iterations: 1);

            return result;
        }

        private bool TryApproximateQuadrilateralCorners(
            Point[] contour,
            out Point2f[] corners)
        {
            corners = Array.Empty<Point2f>();

            if (contour == null || contour.Length < 4)
            {
                return false;
            }

            double perimeter = Cv2.ArcLength(contour, true);

            if (perimeter <= 0)
            {
                return false;
            }

            double[] epsilonRatios = { 0.015, 0.02, 0.03, 0.05 };

            foreach (double epsilonRatio in epsilonRatios)
            {
                Point[] approx = Cv2.ApproxPolyDP(
                    contour,
                    epsilon: perimeter * epsilonRatio,
                    closed: true);

                if (approx.Length == 4 && Cv2.IsContourConvex(approx))
                {
                    corners = approx
                        .Select(point => new Point2f(point.X, point.Y))
                        .ToArray();
                    return true;
                }
            }

            return false;
        }

        private Point2f[] OrderA4CornersForCanonicalView(Point2f[] corners)
        {
            if (corners == null || corners.Length != 4)
            {
                throw new ArgumentException("Exactly 4 corners are required.", nameof(corners));
            }

            Point2f topLeft = corners.OrderBy(point => point.X + point.Y).First();
            Point2f bottomRight = corners.OrderByDescending(point => point.X + point.Y).First();
            Point2f topRight = corners.OrderByDescending(point => point.X - point.Y).First();
            Point2f bottomLeft = corners.OrderBy(point => point.X - point.Y).First();

            Point2f[] ordered =
            {
                topLeft,
                topRight,
                bottomRight,
                bottomLeft
            };

            double topEdgeLength = Distance(ordered[0], ordered[1]);
            double rightEdgeLength = Distance(ordered[1], ordered[2]);

            if (topEdgeLength < rightEdgeLength)
            {
                ordered = new[]
                {
                    ordered[3],
                    ordered[0],
                    ordered[1],
                    ordered[2]
                };
            }

            return ordered;
        }

        private bool TrySelectBestA4Contour(
            Point[][] contours,
            Rect roi,
            out Point[]? selectedContour)
        {
            selectedContour = null;

            double roiArea = roi.Width * roi.Height;

            if (roiArea <= 0)
            {
                return false;
            }

            double bestScore = double.MinValue;

            foreach (Point[] contour in contours)
            {
                if (!TryEvaluateA4ContourCandidate(
                    contour,
                    roiArea,
                    out double score))
                {
                    continue;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    selectedContour = contour;
                }
            }

            return selectedContour != null;
        }

        private bool TryEvaluateA4ContourCandidate(
            Point[] contour,
            double roiArea,
            out double score)
        {
            score = 0;

            double area = Cv2.ContourArea(contour);
            double areaRatio = area / roiArea;

            if (areaRatio < MinA4AreaRatio || areaRatio > MaxA4AreaRatio)
            {
                return false;
            }

            RotatedRect rotatedRect = Cv2.MinAreaRect(contour);
            double width = rotatedRect.Size.Width;
            double height = rotatedRect.Size.Height;

            if (width <= 0 || height <= 0)
            {
                return false;
            }

            double longSide = Math.Max(width, height);
            double shortSide = Math.Min(width, height);
            double aspectRatio = longSide / shortSide;

            if (Math.Abs(aspectRatio - A4AspectRatio) > A4AspectRatioTolerance)
            {
                return false;
            }

            double extent = area / (width * height);

            if (extent < MinA4Extent)
            {
                return false;
            }

            double perimeter = Cv2.ArcLength(contour, true);

            if (perimeter <= 0)
            {
                return false;
            }

            Point[] approx = Cv2.ApproxPolyDP(
                contour,
                epsilon: perimeter * 0.03,
                closed: true);

            if (approx.Length < 4 || approx.Length > 8)
            {
                return false;
            }

            double aspectScore = 1.0 - Math.Min(
                Math.Abs(aspectRatio - A4AspectRatio) / A4AspectRatioTolerance,
                1.0);

            score = (areaRatio * 0.6) + (aspectScore * 0.3) + (extent * 0.1);
            return true;
        }

        private Rect GetSafeInspectionRoi(Mat frame, Rect? inspectionRoi)
        {
            Rect imageBounds = new Rect(0, 0, frame.Width, frame.Height);

            if (!inspectionRoi.HasValue)
            {
                return imageBounds;
            }

            return IntersectRects(inspectionRoi.Value, imageBounds);
        }

        private static double Distance(Point2f p1, Point2f p2)
        {
            double dx = p1.X - p2.X;
            double dy = p1.Y - p2.Y;

            return Math.Sqrt(dx * dx + dy * dy);
        }

        // undistorted frame 좌표계의 점들을 Homography를 이용해 corrected plane 좌표계로 변환한다.
        // sourcePoints는 보통 RotatedRect.Points()에서 얻은 4개 꼭짓점이다.
        private Point2f[] TransformPointsByHomography(Point2f[] sourcePoints, Mat homography)
        {
            if (sourcePoints == null || sourcePoints.Length == 0)
            {
                throw new ArgumentException("Source points are empty.", nameof(sourcePoints));
            }

            if (homography == null || homography.Empty())
            {
                throw new ArgumentException("Homography is empty.", nameof(homography));
            }

            if (homography.Rows != 3 || homography.Cols != 3)
            {
                throw new ArgumentException("Homography must be a 3x3 matrix.", nameof(homography));
            }

            Point2f[] transformedPoints = new Point2f[sourcePoints.Length];

            using var h64 = new Mat();
            homography.ConvertTo(h64, MatType.CV_64FC1);

            double h00 = h64.At<double>(0, 0);
            double h01 = h64.At<double>(0, 1);
            double h02 = h64.At<double>(0, 2);

            double h10 = h64.At<double>(1, 0);
            double h11 = h64.At<double>(1, 1);
            double h12 = h64.At<double>(1, 2);

            double h20 = h64.At<double>(2, 0);
            double h21 = h64.At<double>(2, 1);
            double h22 = h64.At<double>(2, 2);

            for (int i = 0; i < sourcePoints.Length; i++)
            {
                double x = sourcePoints[i].X;
                double y = sourcePoints[i].Y;

                double w = h20 * x + h21 * y + h22;

                if (Math.Abs(w) < 1e-9)
                {
                    throw new InvalidOperationException("Homography transform failed because w is too close to zero.");
                }

                double transformedX = (h00 * x + h01 * y + h02) / w;
                double transformedY = (h10 * x + h11 * y + h12) / w;

                transformedPoints[i] = new Point2f(
                    (float)transformedX,
                    (float)transformedY);
            }

            return transformedPoints;
        }

        // Homography 변환이 끝난 4개 꼭짓점에서 긴 면과 짧은 면을 계산한다.
        // transformedPoints는 RotatedRect.Points()의 순서가 유지된 상태라고 가정한다.
        private void CalculateLongShortSideFromTransformedPoints(
            Point2f[] transformedPoints,
            out double longSidePx,
            out double shortSidePx)
        {
            if (transformedPoints == null || transformedPoints.Length != 4)
            {
                throw new ArgumentException("Exactly 4 transformed points are required.", nameof(transformedPoints));
            }

            double Distance(Point2f p1, Point2f p2)
            {
                double dx = p1.X - p2.X;
                double dy = p1.Y - p2.Y;

                return Math.Sqrt(dx * dx + dy * dy);
            }

            double d01 = Distance(transformedPoints[0], transformedPoints[1]);
            double d12 = Distance(transformedPoints[1], transformedPoints[2]);
            double d23 = Distance(transformedPoints[2], transformedPoints[3]);
            double d30 = Distance(transformedPoints[3], transformedPoints[0]);

            double sideA = (d01 + d23) / 2.0;
            double sideB = (d12 + d30) / 2.0;

            longSidePx = Math.Max(sideA, sideB);
            shortSidePx = Math.Min(sideA, sideB);
        }

        public InspectionResult InspectWithHomography(
    Mat undistortedFrame,
    Mat homography,
    double mmPerPixel,
    double expectedLongMm,
    double expectedShortMm,
    double toleranceMm,
    double expectedHoleDiameterMm,
    double holePositionToleranceMm,
    IReadOnlyList<HoleReference>? holeReferences,
    Rect? inspectionRoi = null)
        {
            try
            {
                if (undistortedFrame == null || undistortedFrame.Empty())
                {
                    return new InspectionResult
                    {
                        State = InspectionState.Error,
                        MeasuredValue = 0,
                        IsInTolerance = false,
                        Timestamp = DateTime.Now
                    };
                }

                if (homography == null || homography.Empty())
                {
                    return new InspectionResult
                    {
                        State = InspectionState.Error,
                        MeasuredValue = 0,
                        IsInTolerance = false,
                        Timestamp = DateTime.Now
                    };
                }

                if (homography.Rows != 3 || homography.Cols != 3)
                {
                    return new InspectionResult
                    {
                        State = InspectionState.Error,
                        MeasuredValue = 0,
                        IsInTolerance = false,
                        Timestamp = DateTime.Now
                    };
                }

                if (mmPerPixel <= 0)
                {
                    return new InspectionResult
                    {
                        State = InspectionState.Error,
                        MeasuredValue = 0,
                        IsInTolerance = false,
                        Timestamp = DateTime.Now
                    };
                }

                if (expectedLongMm <= 0 ||
                    expectedShortMm <= 0 ||
                    toleranceMm < 0 ||
                    expectedHoleDiameterMm <= 0 ||
                    holePositionToleranceMm < 0)
                {
                    return new InspectionResult
                    {
                        State = InspectionState.Error,
                        MeasuredValue = 0,
                        IsInTolerance = false,
                        Timestamp = DateTime.Now
                    };
                }

                if (!TryDetectA4SheetCorners(
                    undistortedFrame,
                    inspectionRoi,
                    out Point2f[] a4Corners))
                {
                    return new InspectionResult
                    {
                        State = InspectionState.NoObject,
                        MeasuredValue = 0,
                        IsInTolerance = false,
                        DetailMessage = "A4 외곽을 감지하지 못했습니다.",
                        Timestamp = DateTime.Now
                    };
                }

                Point2f[] transformedPoints = TransformPointsByHomography(
                    a4Corners,
                    homography);

                CalculateLongShortSideFromTransformedPoints(
                    transformedPoints,
                    out double longSidePx,
                    out double shortSidePx);

                if (!TryConvertPixelSizeToMillimeter(longSidePx, mmPerPixel, out double measuredLongMm))
                {
                    return new InspectionResult
                    {
                        State = InspectionState.Error,
                        MeasuredValue = 0,
                        IsInTolerance = false,
                        Timestamp = DateTime.Now
                    };
                }

                if (!TryConvertPixelSizeToMillimeter(shortSidePx, mmPerPixel, out double measuredShortMm))
                {
                    return new InspectionResult
                    {
                        State = InspectionState.Error,
                        MeasuredValue = 0,
                        IsInTolerance = false,
                        Timestamp = DateTime.Now
                    };
                }

                double pixelsPerMm = 1.0 / mmPerPixel;
                int canonicalWidthPx = Math.Max(1, (int)Math.Round(expectedLongMm * pixelsPerMm));
                int canonicalHeightPx = Math.Max(1, (int)Math.Round(expectedShortMm * pixelsPerMm));

                using Mat a4ToCanonicalHomography = CreateA4CanonicalHomography(
                    a4Corners,
                    canonicalWidthPx,
                    canonicalHeightPx);

                using Mat canonicalA4Frame = CreateCanonicalA4Frame(
                    undistortedFrame,
                    a4ToCanonicalHomography,
                    canonicalWidthPx,
                    canonicalHeightPx);

                List<HoleMeasurement> holeMeasurements = DetectCornerHoles(
                    canonicalA4Frame,
                    pixelsPerMm,
                    expectedHoleDiameterMm,
                    toleranceMm,
                    holePositionToleranceMm,
                    holeReferences);

                DrawA4SheetOverlay(
                    undistortedFrame,
                    a4Corners,
                    a4ToCanonicalHomography,
                    canonicalWidthPx,
                    canonicalHeightPx,
                    pixelsPerMm);

                using Mat canonicalToImageHomography = a4ToCanonicalHomography.Inv();
                DrawDetectedHoleOverlay(
                    undistortedFrame,
                    holeMeasurements,
                    canonicalToImageHomography,
                    pixelsPerMm);

                int detectedHoleCount = holeMeasurements.Count(hole => hole.IsDetected);
                int acceptedHoleCount = holeMeasurements.Count(hole => hole.IsDetected && hole.IsInTolerance);
                bool isInTolerance = acceptedHoleCount == 4;

                return new InspectionResult
                {
                    State = isInTolerance ? InspectionState.Ok : InspectionState.Warning,

                    // 기존 UI 호환용 대표값은 긴 면으로 유지
                    MeasuredValue = measuredLongMm,

                    // Homography 변환 후 corrected plane 기준 측정값
                    MeasuredLongMm = measuredLongMm,
                    MeasuredShortMm = measuredShortMm,
                    MeasuredLongPx = longSidePx,
                    MeasuredShortPx = shortSidePx,
                    IsA4SheetDetected = true,
                    A4CanonicalWidthPx = canonicalWidthPx,
                    A4CanonicalHeightPx = canonicalHeightPx,
                    CornerRoiSizeMm = CornerHoleRoiSizeMm,
                    ExpectedHoleDiameterMm = expectedHoleDiameterMm,
                    HoleMeasurements = holeMeasurements,
                    DetailMessage =
                        $"A4 detected, holes: {acceptedHoleCount}/4 OK ({detectedHoleCount}/4 detected), " +
                        $"diaTol={toleranceMm:F2}mm, posTol={holePositionToleranceMm:F2}mm, " +
                        $"canonical: {canonicalWidthPx}x{canonicalHeightPx}px",

                    IsInTolerance = isInTolerance,
                    Timestamp = DateTime.Now
                };
            }
            catch
            {
                return new InspectionResult
                {
                    State = InspectionState.Error,
                    MeasuredValue = 0,
                    IsInTolerance = false,
                    Timestamp = DateTime.Now
                };
            }
        }


        // sizePx : 검사할 대상이 이미지 안에서 몇 pixel인지
        // sizeMm : sizePx를 mmPerPixel로 변환한 실제 mm단위 측정값
        // ex. sizePx = 83, mmPerPixel = 0.2 -> sizeMm = 16.6mm
        private bool TryConvertPixelSizeToMillimeter(double sizePx, double mmPerPixel, out double sizeMm)
        {
            sizeMm = 0;

            if (sizePx <= 0)
            {
                return false;
            }

            if (mmPerPixel <= 0)
            {
                return false;
            }

            sizeMm = sizePx * mmPerPixel;
            return true;
        }

        // true는 측정값이 허용오차 범위 안에 있음을 의미, false는 범위를 벗어난 경우
        private bool IsWithinTolerance(double measuredValueMm, double minMm, double maxMm)
        {
            if (minMm > maxMm)
            {
                // min이 max보다 큼 -> 잘못된 허용오차 범위
                return false;
            }

            if (measuredValueMm >= minMm && measuredValueMm <= maxMm)
            {
                return true;
            }
            else return false;
        }





        // 여기 아래는 pixel 구하는 코드, 실제론 사용 x

        public bool TryFindTargetRectFromImage(
            string inputImagePath,
            out Rect globalRect,
            out int widthPx,
            out int heightPx)
        {
            globalRect = new Rect(0, 0, 0, 0);
            widthPx = 0;
            heightPx = 0;

            if (string.IsNullOrWhiteSpace(inputImagePath))
            {
                return false;
            }

            if (!File.Exists(inputImagePath))
            {
                return false;
            }

            using Mat src = Cv2.ImRead(inputImagePath, ImreadModes.Color);
            if (src.Empty())
            {
                return false;
            }

            // 1) ROI 설정
            Rect roi = new Rect(1000, 1230, 1200, 1302);

            // ROI가 원본 이미지 범위를 벗어나지 않도록 보정
            Rect imageBounds = new Rect(0, 0, src.Width, src.Height);
            Rect safeRoi = IntersectRects(roi, imageBounds);

            if (safeRoi.Width <= 0 || safeRoi.Height <= 0)
            {
                return false;
            }

            using Mat gray = new();
            using Mat binary = new();
            using Mat morphed = new();
            using Mat roiMat = new Mat(src, safeRoi);

            // 2) ROI 내부 전처리
            Cv2.CvtColor(roiMat, gray, ColorConversionCodes.BGR2GRAY);

            Cv2.Threshold(
                gray,
                binary,
                100,
                255,
                ThresholdTypes.Binary);

            using Mat kernel = Cv2.GetStructuringElement(
                MorphShapes.Rect,
                new OpenCvSharp.Size(3, 3));

            Cv2.MorphologyEx(
                binary,
                morphed,
                MorphTypes.Open,
                kernel,
                iterations: 1);

            // 3) ROI 내부 contour 검출
            Cv2.FindContours(
                morphed,
                out Point[][] contours,
                out HierarchyIndex[] _,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

            Point[]? selectedContour = FindBestContourByRulesVer2(
                contours,
                minArea: 1000,
                minWidth: 30,
                minHeight: 30,
                minX: 10,
                minY: 10);

            if (selectedContour is null)
            {
                return false;
            }

            // 4) ROI 내부 local rect 계산
            Rect localRect = Cv2.BoundingRect(selectedContour);

            if (localRect.Width <= 0 || localRect.Height <= 0)
            {
                return false;
            }

            // 5) 원본 이미지 기준 global rect로 변환
            globalRect = new Rect(
                safeRoi.X + localRect.X,
                safeRoi.Y + localRect.Y,
                localRect.Width,
                localRect.Height);

            widthPx = globalRect.Width;
            heightPx = globalRect.Height;

            return true;
        }

        public void SaveRoiAllContoursDebugOverlay(
            string inputImagePath,
            string outputImagePath)
        {
            using Mat src = Cv2.ImRead(inputImagePath, ImreadModes.Color);
            if (src.Empty())
            {
                throw new InvalidOperationException("이미지를 불러오지 못했습니다.");
            }

            using Mat overlay = src.Clone();
            using Mat gray = new();
            using Mat binary = new();
            using Mat morphed = new();

            // 1) ROI 설정
            Rect roi = new Rect(1000, 1230, 1200, 1302);

            Rect imageBounds = new Rect(0, 0, src.Width, src.Height);
            Rect safeRoi = IntersectRects(roi, imageBounds);

            if (safeRoi.Width <= 0 || safeRoi.Height <= 0)
            {
                Cv2.ImWrite(outputImagePath, overlay);
                return;
            }

            // 2) ROI 잘라내기
            using Mat roiMat = new Mat(src, safeRoi);

            // 3) ROI 내부 전처리
            Cv2.CvtColor(roiMat, gray, ColorConversionCodes.BGR2GRAY);

            Cv2.Threshold(
                gray,
                binary,
                100,
                255,
                ThresholdTypes.Binary);

            using Mat kernel = Cv2.GetStructuringElement(
                MorphShapes.Rect,
                new OpenCvSharp.Size(3, 3));

            Cv2.MorphologyEx(
                binary,
                morphed,
                MorphTypes.Open,
                kernel,
                iterations: 1);

            // 4) ROI 내부 contour 검출
            Cv2.FindContours(
                morphed,
                out Point[][] contours,
                out HierarchyIndex[] hierarchy,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

            Point[]? selectedContour = FindBestContourByRulesVer2(
                contours,
                minArea: 1000,
                minWidth: 30,
                minHeight: 30,
                minX: 10,
                minY: 10);

            // 5) ROI 영역 표시
            Cv2.Rectangle(overlay, safeRoi, Scalar.LimeGreen, 2);

            if (selectedContour is null)
            {
                Cv2.ImWrite(outputImagePath, overlay);
                return;
            }

            Point[] globalContour = selectedContour
                .Select(p => new Point(p.X + safeRoi.X, p.Y + safeRoi.Y))
                .ToArray();

            Rect localRect = Cv2.BoundingRect(selectedContour);
            Rect globalRect = new Rect(
                safeRoi.X + localRect.X,
                safeRoi.Y + localRect.Y,
                localRect.Width,
                localRect.Height);

            Cv2.DrawContours(
                overlay,
                new[] { globalContour },
                contourIdx: -1,
                color: Scalar.Yellow,
                thickness: 2);

            Cv2.Rectangle(
                overlay,
                globalRect,
                Scalar.Red,
                2);

            Cv2.PutText(
                overlay,
                $"W: {globalRect.Width}px, H: {globalRect.Height}px",
                new Point(globalRect.X, Math.Max(globalRect.Y - 10, 30)),
                HersheyFonts.HersheySimplex,
                1.0,
                Scalar.Cyan,
                2);

            Cv2.ImWrite(outputImagePath, overlay);
        }

        public Point[]? FindBestContourByRulesVer2(
            Point[][] contours,
            double minArea,
            int minWidth,
            int minHeight,
            int minX,
            int minY)
        {
            Point[]? bestContour = null;
            double bestArea = 0;

            foreach (Point[] contour in contours)
            {
                double area = Cv2.ContourArea(contour);
                if (area < minArea)
                    continue;

                Rect rect = Cv2.BoundingRect(contour);

                if (rect.Width < minWidth)
                    continue;

                if (rect.Height < minHeight)
                    continue;

                if (rect.X < minX)
                    continue;

                if (rect.Y < minY)
                    continue;

                if (area > bestArea)
                {
                    bestArea = area;
                    bestContour = contour;
                }
            }

            return bestContour;
        }

        private Rect IntersectRects(Rect a, Rect b)
        {
            int x1 = Math.Max(a.X, b.X);
            int y1 = Math.Max(a.Y, b.Y);
            int x2 = Math.Min(a.X + a.Width, b.X + b.Width);
            int y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);

            if (x2 <= x1 || y2 <= y1)
            {
                return new Rect(0, 0, 0, 0);
            }

            return new Rect(x1, y1, x2 - x1, y2 - y1);
        }

        // 1차 실험 과정에서 객체 인식 상태 불안정 발생, 디버깅 용도
        private void DrawDebugOverlay(Mat frame, Rect targetRect)
        {
            if (frame == null || frame.Empty())
            {
                return;
            }

            if (targetRect.Width <= 0 || targetRect.Height <= 0)
            {
                return;
            }

            double longSidePx = Math.Max(targetRect.Width, targetRect.Height);
            double shortSidePx = Math.Min(targetRect.Width, targetRect.Height);

            Cv2.Rectangle(frame, targetRect, Scalar.Red, 2);

            string label = $"W:{targetRect.Width}px H:{targetRect.Height}px  L:{longSidePx:F0} S:{shortSidePx:F0}";
            Point textPoint = new Point(targetRect.X, Math.Max(targetRect.Y - 10, 30));

            Cv2.PutText(
                frame,
                label,
                textPoint,
                HersheyFonts.HersheySimplex,
                0.8,
                Scalar.Yellow,
                2);
        }

        private Mat CreateA4CanonicalHomography(
            Point2f[] orderedCorners,
            int canonicalWidthPx,
            int canonicalHeightPx)
        {
            if (orderedCorners == null || orderedCorners.Length != 4)
            {
                throw new ArgumentException("Exactly 4 A4 corners are required.", nameof(orderedCorners));
            }

            Point2f[] canonicalCorners =
            {
                new(0, 0),
                new(canonicalWidthPx - 1, 0),
                new(canonicalWidthPx - 1, canonicalHeightPx - 1),
                new(0, canonicalHeightPx - 1)
            };

            return Cv2.GetPerspectiveTransform(orderedCorners, canonicalCorners);
        }

        private Mat CreateCanonicalA4Frame(
            Mat sourceFrame,
            Mat a4ToCanonicalHomography,
            int canonicalWidthPx,
            int canonicalHeightPx)
        {
            var canonicalFrame = new Mat();

            Cv2.WarpPerspective(
                sourceFrame,
                canonicalFrame,
                a4ToCanonicalHomography,
                new Size(canonicalWidthPx, canonicalHeightPx));

            return canonicalFrame;
        }

        private List<HoleMeasurement> DetectCornerHoles(
            Mat canonicalA4Frame,
            double pixelsPerMm,
            double expectedHoleDiameterMm,
            double diameterToleranceMm,
            double positionToleranceMm,
            IReadOnlyList<HoleReference>? holeReferences)
        {
            var measurements = new List<HoleMeasurement>();

            string[] cornerNames = { "LT", "RT", "RB", "LB" };
            Rect[] cornerRois = CreateCornerHoleRoiRects(
                canonicalA4Frame.Width,
                canonicalA4Frame.Height,
                pixelsPerMm);

            for (int i = 0; i < cornerRois.Length; i++)
            {
                measurements.Add(DetectHoleInCornerRoi(
                    canonicalA4Frame,
                    cornerRois[i],
                    cornerNames[i],
                    pixelsPerMm,
                    expectedHoleDiameterMm,
                    diameterToleranceMm,
                    positionToleranceMm,
                    FindHoleReference(holeReferences, cornerNames[i])));
            }

            return measurements;
        }

        private static HoleReference? FindHoleReference(
            IReadOnlyList<HoleReference>? holeReferences,
            string cornerName)
        {
            if (holeReferences == null || holeReferences.Count == 0)
            {
                return null;
            }

            return holeReferences.FirstOrDefault(reference =>
                string.Equals(
                    reference.CornerName,
                    cornerName,
                    StringComparison.OrdinalIgnoreCase));
        }

        private HoleMeasurement DetectHoleInCornerRoi(
            Mat canonicalA4Frame,
            Rect roi,
            string cornerName,
            double pixelsPerMm,
            double expectedHoleDiameterMm,
            double diameterToleranceMm,
            double positionToleranceMm,
            HoleReference? holeReference)
        {
            if (roi.Width <= 0 || roi.Height <= 0)
            {
                return new HoleMeasurement
                {
                    CornerName = cornerName,
                    IsDetected = false,
                    IsInTolerance = false,
                    Message = "ROI invalid"
                };
            }

            using var roiFrame = new Mat(canonicalA4Frame, roi);
            using var gray = new Mat();
            using var blurred = new Mat();
            using var binary = new Mat();
            using var morph = new Mat();

            Cv2.CvtColor(roiFrame, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);

            Cv2.Threshold(
                blurred,
                binary,
                0,
                255,
                ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);

            using Mat kernel = Cv2.GetStructuringElement(
                MorphShapes.Ellipse,
                new Size(3, 3));

            Cv2.MorphologyEx(
                binary,
                morph,
                MorphTypes.Open,
                kernel,
                iterations: 1);

            Cv2.FindContours(
                morph,
                out Point[][] contours,
                out HierarchyIndex[] _,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

            if (contours == null || contours.Length == 0)
            {
                return new HoleMeasurement
                {
                    CornerName = cornerName,
                    IsDetected = false,
                    IsInTolerance = false,
                    Message = "hole not found"
                };
            }

            List<HoleCandidate> candidates = FindHoleCandidates(
                contours,
                roi,
                pixelsPerMm,
                expectedHoleDiameterMm);

            if (candidates.Count == 0)
            {
                return new HoleMeasurement
                {
                    CornerName = cornerName,
                    ExpectedCenterXmm = holeReference?.CenterXmm ?? 0,
                    ExpectedCenterYmm = holeReference?.CenterYmm ?? 0,
                    IsDetected = false,
                    IsInTolerance = false,
                    CandidateCount = 0,
                    Message = "no valid hole candidate"
                };
            }

            HoleCandidate selectedCandidate = SelectBestHoleCandidate(
                candidates,
                holeReference,
                expectedHoleDiameterMm,
                positionToleranceMm);

            double expectedCenterXmm = holeReference?.CenterXmm ?? selectedCandidate.CenterXmm;
            double expectedCenterYmm = holeReference?.CenterYmm ?? selectedCandidate.CenterYmm;
            double positionErrorMm = holeReference == null
                ? 0
                : DistanceMm(
                    selectedCandidate.CenterXmm,
                    selectedCandidate.CenterYmm,
                    holeReference.CenterXmm,
                    holeReference.CenterYmm);

            double referenceDiameterMm = holeReference?.DiameterMm > 0
                ? holeReference.DiameterMm
                : expectedHoleDiameterMm;

            double diameterError = Math.Abs(selectedCandidate.DiameterMm - referenceDiameterMm);
            int extraHoleCount = CountExtraHoleCandidates(
                candidates,
                selectedCandidate,
                referenceDiameterMm,
                diameterToleranceMm);
            bool isDiameterInTolerance = diameterError <= diameterToleranceMm;
            bool isCircularityInTolerance = selectedCandidate.Circularity >= MinHoleCircularity;
            bool isPositionInTolerance =
                holeReference == null ||
                positionErrorMm <= positionToleranceMm;
            bool isInTolerance =
                isDiameterInTolerance &&
                isCircularityInTolerance &&
                isPositionInTolerance;

            string message;

            if (!isDiameterInTolerance)
            {
                message = "diameter NG";
            }
            else if (!isCircularityInTolerance)
            {
                message = "circularity NG";
            }
            else if (!isPositionInTolerance)
            {
                message = "position NG";
            }
            else
            {
                message = "detected";
            }

            return new HoleMeasurement
            {
                CornerName = cornerName,
                ExpectedCenterXmm = expectedCenterXmm,
                ExpectedCenterYmm = expectedCenterYmm,
                IsDetected = true,
                CenterXmm = selectedCandidate.CenterXmm,
                CenterYmm = selectedCandidate.CenterYmm,
                PositionErrorMm = positionErrorMm,
                DiameterMm = selectedCandidate.DiameterMm,
                DiameterErrorMm = diameterError,
                Circularity = selectedCandidate.Circularity,
                CandidateCount = candidates.Count,
                ExtraHoleCount = extraHoleCount,
                IsInTolerance = isInTolerance,
                Message = message
            };
        }

        private int CountExtraHoleCandidates(
            List<HoleCandidate> candidates,
            HoleCandidate selectedCandidate,
            double referenceDiameterMm,
            double diameterToleranceMm)
        {
            int count = 0;

            foreach (HoleCandidate candidate in candidates)
            {
                if (ReferenceEquals(candidate, selectedCandidate))
                {
                    continue;
                }

                double diameterErrorMm = Math.Abs(candidate.DiameterMm - referenceDiameterMm);

                if (diameterErrorMm <= diameterToleranceMm &&
                    candidate.Circularity >= MinHoleCircularity)
                {
                    count++;
                }
            }

            return count;
        }

        private List<HoleCandidate> FindHoleCandidates(
            Point[][] contours,
            Rect roi,
            double pixelsPerMm,
            double expectedHoleDiameterMm)
        {
            var candidates = new List<HoleCandidate>();
            double expectedDiameterPx = expectedHoleDiameterMm * pixelsPerMm;
            double expectedAreaPx = Math.PI * Math.Pow(expectedDiameterPx / 2.0, 2);

            foreach (Point[] contour in contours)
            {
                double area = Cv2.ContourArea(contour);

                if (area < expectedAreaPx * 0.15 || area > expectedAreaPx * 4.0)
                {
                    continue;
                }

                double perimeter = Cv2.ArcLength(contour, true);

                if (perimeter <= 0)
                {
                    continue;
                }

                Moments moments = Cv2.Moments(contour);

                if (Math.Abs(moments.M00) < 1e-9)
                {
                    continue;
                }

                double circularity = 4.0 * Math.PI * area / (perimeter * perimeter);
                double diameterPx = 2.0 * Math.Sqrt(area / Math.PI);
                double centerXPx = roi.X + (moments.M10 / moments.M00);
                double centerYPx = roi.Y + (moments.M01 / moments.M00);

                candidates.Add(new HoleCandidate
                {
                    CenterXmm = centerXPx / pixelsPerMm,
                    CenterYmm = centerYPx / pixelsPerMm,
                    DiameterMm = diameterPx / pixelsPerMm,
                    Circularity = circularity
                });
            }

            return candidates;
        }

        private HoleCandidate SelectBestHoleCandidate(
            List<HoleCandidate> candidates,
            HoleReference? holeReference,
            double expectedHoleDiameterMm,
            double positionToleranceMm)
        {
            if (holeReference == null)
            {
                return candidates
                    .OrderBy(candidate => Math.Abs(candidate.DiameterMm - expectedHoleDiameterMm))
                    .ThenByDescending(candidate => candidate.Circularity)
                    .First();
            }

            return candidates
                .OrderBy(candidate => CalculateReferenceSelectionScore(
                    candidate,
                    holeReference,
                    expectedHoleDiameterMm,
                    positionToleranceMm))
                .First();
        }

        private static double CalculateReferenceSelectionScore(
            HoleCandidate candidate,
            HoleReference reference,
            double expectedHoleDiameterMm,
            double positionToleranceMm)
        {
            double positionErrorMm = DistanceMm(
                candidate.CenterXmm,
                candidate.CenterYmm,
                reference.CenterXmm,
                reference.CenterYmm);

            double referenceDiameterMm = reference.DiameterMm > 0
                ? reference.DiameterMm
                : expectedHoleDiameterMm;

            double diameterErrorMm = Math.Abs(candidate.DiameterMm - referenceDiameterMm);
            double normalizedPositionError = positionErrorMm / Math.Max(positionToleranceMm, 0.001);
            double normalizedDiameterError = diameterErrorMm / Math.Max(expectedHoleDiameterMm, 0.001);
            double circularityPenalty = 1.0 - Math.Min(candidate.Circularity, 1.0);

            return (normalizedPositionError * 0.7) +
                (normalizedDiameterError * 0.2) +
                (circularityPenalty * 0.1);
        }

        private static double DistanceMm(
            double x1,
            double y1,
            double x2,
            double y2)
        {
            double dx = x1 - x2;
            double dy = y1 - y2;

            return Math.Sqrt(dx * dx + dy * dy);
        }

        private sealed class HoleCandidate
        {
            public double CenterXmm { get; set; }
            public double CenterYmm { get; set; }
            public double DiameterMm { get; set; }
            public double Circularity { get; set; }
        }

        private Rect[] CreateCornerHoleRoiRects(
            int canonicalWidthPx,
            int canonicalHeightPx,
            double pixelsPerMm)
        {
            int roiSizePx = Math.Max(
                1,
                (int)Math.Round(Math.Min(
                    CornerHoleRoiSizeMm * pixelsPerMm,
                    Math.Min(canonicalWidthPx, canonicalHeightPx) * 0.4)));

            return new[]
            {
                new Rect(0, 0, roiSizePx, roiSizePx),
                new Rect(canonicalWidthPx - roiSizePx, 0, roiSizePx, roiSizePx),
                new Rect(canonicalWidthPx - roiSizePx, canonicalHeightPx - roiSizePx, roiSizePx, roiSizePx),
                new Rect(0, canonicalHeightPx - roiSizePx, roiSizePx, roiSizePx)
            };
        }

        private void DrawA4SheetOverlay(
            Mat frame,
            Point2f[] orderedCorners,
            Mat a4ToCanonicalHomography,
            int canonicalWidthPx,
            int canonicalHeightPx,
            double pixelsPerMm)
        {
            if (frame == null || frame.Empty())
            {
                return;
            }

            DrawPolygon(frame, orderedCorners, Scalar.LimeGreen, 3);
            DrawCornerLabels(frame, orderedCorners);
            DrawCornerHoleRois(
                frame,
                a4ToCanonicalHomography,
                canonicalWidthPx,
                canonicalHeightPx,
                pixelsPerMm);
        }

        private void DrawCornerLabels(Mat frame, Point2f[] orderedCorners)
        {
            string[] labels = { "A4-LT", "A4-RT", "A4-RB", "A4-LB" };

            for (int i = 0; i < orderedCorners.Length; i++)
            {
                Point textPoint = new(
                    (int)Math.Round(orderedCorners[i].X),
                    (int)Math.Round(orderedCorners[i].Y));

                Cv2.PutText(
                    frame,
                    labels[i],
                    textPoint,
                    HersheyFonts.HersheySimplex,
                    0.6,
                    Scalar.Yellow,
                    2);
            }
        }

        private void DrawCornerHoleRois(
            Mat frame,
            Mat a4ToCanonicalHomography,
            int canonicalWidthPx,
            int canonicalHeightPx,
            double pixelsPerMm)
        {
            if (a4ToCanonicalHomography == null || a4ToCanonicalHomography.Empty())
            {
                return;
            }

            double roiSizePx = Math.Min(
                CornerHoleRoiSizeMm * pixelsPerMm,
                Math.Min(canonicalWidthPx, canonicalHeightPx) * 0.4);

            Point2f[][] canonicalRois =
            {
                CreateCanonicalRectPoints(0, 0, roiSizePx, roiSizePx),
                CreateCanonicalRectPoints(canonicalWidthPx - roiSizePx, 0, roiSizePx, roiSizePx),
                CreateCanonicalRectPoints(canonicalWidthPx - roiSizePx, canonicalHeightPx - roiSizePx, roiSizePx, roiSizePx),
                CreateCanonicalRectPoints(0, canonicalHeightPx - roiSizePx, roiSizePx, roiSizePx)
            };

            using Mat canonicalToA4Homography = a4ToCanonicalHomography.Inv();

            foreach (Point2f[] canonicalRoi in canonicalRois)
            {
                Point2f[] imageRoi = TransformPointsByHomography(
                    canonicalRoi,
                    canonicalToA4Homography);

                DrawPolygon(frame, imageRoi, Scalar.Cyan, 2);
            }
        }

        private void DrawDetectedHoleOverlay(
            Mat frame,
            List<HoleMeasurement> holeMeasurements,
            Mat canonicalToImageHomography,
            double pixelsPerMm)
        {
            if (holeMeasurements == null || holeMeasurements.Count == 0)
            {
                return;
            }

            foreach (HoleMeasurement hole in holeMeasurements)
            {
                if (!hole.IsDetected)
                {
                    continue;
                }

                float centerXPx = (float)(hole.CenterXmm * pixelsPerMm);
                float centerYPx = (float)(hole.CenterYmm * pixelsPerMm);
                float radiusPx = (float)((hole.DiameterMm * pixelsPerMm) / 2.0);

                Point2f[] transformed = TransformPointsByHomography(
                    new[]
                    {
                        new Point2f(centerXPx, centerYPx),
                        new Point2f(centerXPx + radiusPx, centerYPx)
                    },
                    canonicalToImageHomography);

                Point center = new(
                    (int)Math.Round(transformed[0].X),
                    (int)Math.Round(transformed[0].Y));

                int radius = Math.Max(
                    4,
                    (int)Math.Round(Distance(transformed[0], transformed[1])));

                Scalar color = hole.IsInTolerance ? Scalar.LimeGreen : Scalar.Red;

                Cv2.Circle(
                    frame,
                    center,
                    radius,
                    color,
                    2);

                Cv2.PutText(
                    frame,
                    $"{hole.CornerName} d:{hole.DiameterMm:F1}",
                    new Point(center.X + 6, center.Y - 6),
                    HersheyFonts.HersheySimplex,
                    0.55,
                    color,
                    2);
            }
        }

        private static Point2f[] CreateCanonicalRectPoints(
            double x,
            double y,
            double width,
            double height)
        {
            return new[]
            {
                new Point2f((float)x, (float)y),
                new Point2f((float)(x + width), (float)y),
                new Point2f((float)(x + width), (float)(y + height)),
                new Point2f((float)x, (float)(y + height))
            };
        }

        private static void DrawPolygon(
            Mat frame,
            Point2f[] points,
            Scalar color,
            int thickness)
        {
            if (points == null || points.Length < 2)
            {
                return;
            }

            Point[] intPoints = points
                .Select(point => new Point(
                    (int)Math.Round(point.X),
                    (int)Math.Round(point.Y)))
                .ToArray();

            for (int i = 0; i < intPoints.Length; i++)
            {
                Cv2.Line(
                    frame,
                    intPoints[i],
                    intPoints[(i + 1) % intPoints.Length],
                    color,
                    thickness);
            }
        }

        private void DrawRotatedRectOverlay(
            Mat frame,
            RotatedRect targetRect,
            double measuredLongMm,
            double measuredShortMm)
        {
            if (frame == null || frame.Empty())
            {
                return;
            }

            Point2f[] points = targetRect.Points();

            if (points == null || points.Length != 4)
            {
                return;
            }

            Point[] intPoints = new Point[4];

            for (int i = 0; i < points.Length; i++)
            {
                intPoints[i] = new Point(
                    (int)Math.Round(points[i].X),
                    (int)Math.Round(points[i].Y));
            }

            for (int i = 0; i < 4; i++)
            {
                Point p1 = intPoints[i];
                Point p2 = intPoints[(i + 1) % 4];

                Cv2.Line(
                    frame,
                    p1,
                    p2,
                    Scalar.Red,
                    2);
            }

            Point textPoint = new Point(
                (int)Math.Round(targetRect.Center.X),
                Math.Max((int)Math.Round(targetRect.Center.Y) - 10, 30));

            string label = $"L:{measuredLongMm:F1}mm S:{measuredShortMm:F1}mm";

            Cv2.PutText(
                frame,
                label,
                textPoint,
                HersheyFonts.HersheySimplex,
                0.8,
                Scalar.Yellow,
                2);
        }
    }
}
