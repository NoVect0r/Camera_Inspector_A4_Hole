using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Media.TextFormatting;

namespace Camera_Insepctor_Project.Services
{
    internal class ImageStudyService
    {
        public void SaveGrayscaleImage(string inputPath, string outputPath)
        {
            if (string.IsNullOrEmpty(inputPath))
            {
                throw new ArgumentException("입력 이미지 경로가 비어있습니다", nameof(inputPath));
            }

            if (!File.Exists(inputPath))
            {
                throw new FileNotFoundException("입력 이미지 파일을 찾을 수 없습니다", inputPath);
            }

            using Mat colorMat = Cv2.ImRead(inputPath, ImreadModes.Color);

            if (colorMat.Empty())
            {
                throw new InvalidOperationException("이미지를 Mat으로 읽어왔지만 비어있습니다.");
            }

            using Mat grayMat = new Mat();
            Cv2.CvtColor(colorMat, grayMat, ColorConversionCodes.BGR2GRAY);

            Cv2.ImWrite(outputPath, grayMat);
        }

        public void SaveThresholdImage(string inputPath, string outputPath, double thresholdValue)
        {
            if (string.IsNullOrEmpty(inputPath))
            {
                throw new ArgumentException("입력 이미지 경로가 비어있습니다", nameof(inputPath));
            }

            if (!File.Exists(inputPath))
            {
                throw new FileNotFoundException("입력 이미지 파일을 찾을 수 없습니다", inputPath);
            }

            using Mat colorMat = Cv2.ImRead(inputPath, ImreadModes.Color);

            if (colorMat.Empty())
            {
                throw new InvalidOperationException("이미지를 Mat으로 읽어왔지만 비어있습니다.");
            }

            using Mat grayMat = new Mat();
            Cv2.CvtColor(colorMat, grayMat, ColorConversionCodes.BGR2GRAY);

            using Mat binaryMat = new Mat();
            Cv2.Threshold(grayMat, binaryMat, thresholdValue, 255, ThresholdTypes.BinaryInv);
            Cv2.ImWrite(outputPath, binaryMat);
        }

        public void FindContoursOnly(string inputPath)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
                throw new ArgumentException("입력 이미지 경로가 비어 있습니다.", nameof(inputPath));

            if (!File.Exists(inputPath))
                throw new FileNotFoundException("입력 이미지 파일을 찾을 수 없습니다.", inputPath);

            using Mat colorMat = Cv2.ImRead(inputPath, ImreadModes.Color);

            if (colorMat.Empty())
                throw new InvalidOperationException("이미지를 Mat으로 로드했지만 비어 있습니다.");

            using Mat grayMat = new Mat();
            Cv2.CvtColor(colorMat, grayMat, ColorConversionCodes.BGR2GRAY);

            using Mat binaryMat = new Mat();
            Cv2.Threshold(grayMat, binaryMat, 100, 255, ThresholdTypes.Binary);

            Cv2.FindContours(
                image: binaryMat,
                contours: out Point[][] contours,
                hierarchy: out HierarchyIndex[] hierarchy,
                mode: RetrievalModes.External,
                method: ContourApproximationModes.ApproxSimple
                );

            System.Diagnostics.Debug.WriteLine("Debug Start");
            Debug.WriteLine($"Contours count: {contours.Length}");

            if (contours.Length > 0)
            {
                Debug.WriteLine($"First contour point count: {contours[99].Length}");
            }

            System.Diagnostics.Debug.WriteLine("Debug End");
        }

        public void SaveAllContoursOverlay(string inputPath, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
                throw new ArgumentException("입력 이미지 경로가 비어 있습니다.", nameof(inputPath));

            if (!File.Exists(inputPath))
                throw new FileNotFoundException("입력 이미지 파일을 찾을 수 없습니다.", inputPath);

            using Mat colorMat = Cv2.ImRead(inputPath, ImreadModes.Color);

            if (colorMat.Empty())
                throw new InvalidOperationException("이미지를 Mat으로 로드했지만 비어 있습니다.");

            using Mat grayMat = new Mat();
            Cv2.CvtColor(colorMat, grayMat, ColorConversionCodes.BGR2GRAY);

            using Mat binaryMat = new Mat();
            Cv2.Threshold(grayMat, binaryMat, 100, 255, ThresholdTypes.BinaryInv);

            Cv2.FindContours(
                image: binaryMat,
                contours: out Point[][] contours,
                hierarchy: out HierarchyIndex[] hierarchy,
                mode: RetrievalModes.External,
                method: ContourApproximationModes.ApproxSimple);

            Debug.WriteLine($"Contours count: {contours.Length}");

            for (int i = 0; i < contours.Length; i++)
            {
                double area = Cv2.ContourArea(contours[i]);

                if (area < 5)
                {
                    continue;
                }
                Debug.WriteLine($"Contour[{i}] Area: {area}");

                Cv2.DrawContours(
                    image: colorMat,
                    contours: contours,
                    contourIdx: i,
                    color: Scalar.Red,
                    thickness: 2);
            }

            Cv2.ImWrite(outputPath, colorMat);
            Debug.WriteLine($"All contours overlay saved: {outputPath}");
        }

        public void SaveLargestContourBoundingRectOverlay(string inputPath, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
                throw new ArgumentException("입력 이미지 경로가 비어 있습니다.", nameof(inputPath));

            if (!File.Exists(inputPath))
                throw new FileNotFoundException("입력 이미지 파일을 찾을 수 없습니다.", inputPath);

            using Mat colorMat = Cv2.ImRead(inputPath, ImreadModes.Color);

            if (colorMat.Empty())
                throw new InvalidOperationException("이미지를 Mat으로 로드했지만 비어 있습니다.");

            using Mat grayMat = new Mat();
            Cv2.CvtColor(colorMat, grayMat, ColorConversionCodes.BGR2GRAY);

            using Mat binaryMat = new Mat();
            Cv2.Threshold(grayMat, binaryMat, 100, 255, ThresholdTypes.BinaryInv);

            Cv2.FindContours(
                image: binaryMat,
                contours: out Point[][] contours,
                hierarchy: out HierarchyIndex[] hierarchy,
                mode: RetrievalModes.External,
                method: ContourApproximationModes.ApproxSimple);

            Debug.WriteLine($"Contours count: {contours.Length}");

            if (contours.Length == 0)
                throw new InvalidOperationException("검출된 contour가 없습니다.");

            int largestIndex = 0;
            double largestArea = Cv2.ContourArea(contours[0]);

            for (int i = 1; i < contours.Length; i++)
            {
                double area = Cv2.ContourArea(contours[i]);

                if (area > largestArea)
                {
                    largestArea = area;
                    largestIndex = i;
                }
            }

            Rect rect = Cv2.BoundingRect(contours[largestIndex]);

            Cv2.Rectangle(
                img: colorMat,
                rect: rect,
                color: Scalar.Red,
                thickness: 2);

            Cv2.ImWrite(outputPath, colorMat);

            Debug.WriteLine($"Largest contour index: {largestIndex}");
            Debug.WriteLine($"Largest contour area: {largestArea}");
            Debug.WriteLine($"Rect X={rect.X}, Y={rect.Y}, W={rect.Width}, H={rect.Height}");
            Debug.WriteLine($"Overlay saved: {outputPath}");
        }

        public void SaveFilteredLargestContourBoundingRectOverlay(
            string inputPath,
            string outputPath,
            double thresholdValue = 100,
            double minArea = 5)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
                throw new ArgumentException("입력 이미지 경로가 비어 있습니다.", nameof(inputPath));

            if (!File.Exists(inputPath))
                throw new FileNotFoundException("입력 이미지 파일을 찾을 수 없습니다.", inputPath);

            using Mat colorMat = Cv2.ImRead(inputPath, ImreadModes.Color);

            if (colorMat.Empty())
                throw new InvalidOperationException("이미지를 Mat으로 로드했지만 비어 있습니다.");

            using Mat grayMat = new Mat();
            Cv2.CvtColor(colorMat, grayMat, ColorConversionCodes.BGR2GRAY);

            using Mat binaryMat = new Mat();
            Cv2.Threshold(grayMat, binaryMat, thresholdValue, 255, ThresholdTypes.BinaryInv);

            Cv2.FindContours(
                image: binaryMat,
                contours: out Point[][] contours,
                hierarchy: out HierarchyIndex[] hierarchy,
                mode: RetrievalModes.External,
                method: ContourApproximationModes.ApproxSimple);

            Debug.WriteLine($"Contours count: {contours.Length}");

            int selectedIndex = -1;
            double largestArea = 0;

            for (int i = 0; i < contours.Length; i++)
            {
                double area = Cv2.ContourArea(contours[i]);
                Debug.WriteLine($"Contour[{i}] Area: {area}");

                if (area < minArea)
                    continue;

                if (selectedIndex == -1 || area > largestArea)
                {
                    selectedIndex = i;
                    largestArea = area;
                }
            }

            if (selectedIndex == -1)
                throw new InvalidOperationException("minArea 이상인 contour를 찾지 못했습니다.");

            Rect rect = Cv2.BoundingRect(contours[selectedIndex]);

            Cv2.Rectangle(
                img: colorMat,
                rect: rect,
                color: Scalar.Red,
                thickness: 2);

            Cv2.ImWrite(outputPath, colorMat);

            Debug.WriteLine($"Selected contour index: {selectedIndex}");
            Debug.WriteLine($"Selected contour area: {largestArea}");
            Debug.WriteLine($"Rect X={rect.X}, Y={rect.Y}, W={rect.Width}, H={rect.Height}");
            Debug.WriteLine($"Overlay saved: {outputPath}");
        }

        public void SaveSelectedContourBoundingRectOverlay(
        string inputImagePath,
        string outputImagePath)
        {
            using Mat src = Cv2.ImRead(inputImagePath, ImreadModes.Color);
            using Mat gray = new Mat();
            using Mat binary = new Mat();

            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

            // 예시 threshold 값. 이미지에 맞게 조정 필요
            Cv2.Threshold(gray, binary, 100, 255, ThresholdTypes.BinaryInv);

            Cv2.FindContours(
                binary,
                out Point[][] contours,
                out HierarchyIndex[] hierarchy,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

            Point[]? bestContour = FindBestContourByRules_ver1(
                contours,
                minArea: 5000,
                minY: 500,
                minWidth: 500,
                maxWidth: 1000,
                minHeight: 500,
                maxHeight: 1000);

            if (bestContour == null)
            {
                Debug.WriteLine("조건을 만족하는 contour를 찾지 못했습니다.");
                return;
            }

            SaveBoundingRectOverlay(src, bestContour, outputImagePath);
            Debug.WriteLine($"결과 저장 완료: {outputImagePath}");
        }

        public void SaveBoundingRectOverlay(
            Mat SourceImage,
            Point[] selectedContour,
            string outputImagePath)
        {
            using Mat overlay = SourceImage.Clone();

            Rect rect = Cv2.BoundingRect(selectedContour);

            Cv2.Rectangle(
                img: overlay,
                rect: rect,
                color: Scalar.Red,
                thickness: 2);

            Cv2.ImWrite(outputImagePath, overlay);

            Debug.WriteLine($"Rect X={rect.X}, Y={rect.Y}, W={rect.Width}, H={rect.Height}");
            Debug.WriteLine($"Overlay saved: {outputImagePath}");
        }

        public Point[]? FindBestContourByRules_ver1(
            Point[][] contours,
            double minArea,
            int minY,
            int minWidth,
            int maxWidth,
            int minHeight,
            int maxHeight
            )
        {
            Point[]? bestContour = null;
            double bestArea = 0;

            foreach (Point[] contour in contours)
            {
                double area = Cv2.ContourArea(contour);
                if (area < minArea)
                    continue;
                Rect rect = Cv2.BoundingRect(contour);
                if (rect.Y < minY)
                    continue;
                if (rect.Width < minWidth || rect.Width > maxWidth)
                    continue;
                if (rect.Height < minHeight || rect.Height > maxHeight)
                    continue;
                if (bestContour == null || area > bestArea)
                {
                    bestContour = contour;
                    bestArea = area;
                }
            }
            return bestContour;
        }

        public void SaveMorphologyResult(
            string inputImagePath,
            string outputImagePath)
        {
            using Mat src = Cv2.ImRead(inputImagePath, ImreadModes.Color);
            if (src.Empty())
            {
                throw new InvalidOperationException("이미지를 불러오지 못했습니다.");
            }

            using Mat gray = new();
            using Mat binary = new();
            using Mat morphed = new();

            // 1) Gray
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

            // 2) Threshold
            Cv2.Threshold(
                gray,
                binary,
                100,
                255,
                ThresholdTypes.Binary);

            // 3) Kernel 생성
            using Mat kernel = Cv2.GetStructuringElement(
                MorphShapes.Rect,
                new Size(3, 3));

            // 4) Morphology
            Cv2.MorphologyEx(
                binary,
                morphed,
                MorphTypes.Open,
                kernel,
                iterations: 1);

            // 5) 결과 저장
            Cv2.ImWrite(outputImagePath, morphed);
        }

        public void SaveRoiContourBoundingRectOverlay(
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

            // 1) ROI 설정 (예시값)
            Rect roi = new Rect(0, 0, 3024, 4032);

            // 2) ROI 영역 잘라내기
            using Mat roiMat = new Mat(src, roi);

            // 3) ROI 내부에서 전처리
            Cv2.CvtColor(roiMat, gray, ColorConversionCodes.BGR2GRAY);

            Cv2.Threshold(
                gray,
                binary,
                100,
                255,
                ThresholdTypes.Binary);

            using Mat kernel = Cv2.GetStructuringElement(
                MorphShapes.Rect,
                new Size(3, 3));

            Cv2.MorphologyEx(
                binary,
                morphed,
                MorphTypes.Open,
                kernel,
                iterations: 1);

            // 4) ROI 내부 contour 찾기
            Cv2.FindContours(
                morphed,
                out Point[][] contours,
                out HierarchyIndex[] hierarchy,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

            Point[]? selectedContour = FindBestContourByRules_ver1(
                contours,
                minArea: 1000,
                minY: 10,
                minWidth: 30,
                maxWidth: 3000,
                minHeight: 30,
                maxHeight: 3000);

            Cv2.Rectangle(overlay, roi, Scalar.LimeGreen, 2);

            if (selectedContour is null)
            {
                Cv2.ImWrite(outputImagePath, overlay);
                Debug.WriteLine("SelectedContour가 없습니다.");
                return;
            }

            Point[] globalContour = selectedContour
            .Select(p => new Point(p.X + roi.X, p.Y + roi.Y))
            .ToArray();

            Cv2.DrawContours(
            overlay,
            new[] { globalContour },
            contourIdx: -1,
            color: Scalar.Yellow,
            thickness: 2);

            Rect localRect = Cv2.BoundingRect(selectedContour);

            Rect globalRect = new Rect(
                roi.X + localRect.X,
                roi.Y + localRect.Y,
                localRect.Width,
                localRect.Height);

            Cv2.Rectangle(overlay, globalRect, Scalar.Red, 2);
            Cv2.ImWrite(outputImagePath, overlay);
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

            // 2) ROI 잘라내기
            using Mat roiMat = new Mat(src, roi);

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
                new Size(3, 3));

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

            Point[]? selectedContour = FindBestContourByRules_ver2(
                contours,
                minArea: 1000,
                minWidth: 30,
                minHeight: 30,
                minX : 10,
                minY : 10);

            // 5) ROI 영역 표시
            Cv2.Rectangle(overlay, roi, Scalar.LimeGreen, 2);

            if (selectedContour is null)
            {
                Cv2.ImWrite(outputImagePath, overlay);
                return;
            }

            /*
            // 6) 모든 contour를 순회하며 디버그 표시
            for (int i = 0; i < contours.Length; i++)
            {
                Point[] localContour = contours[i];

                // contour 면적
                double area = Cv2.ContourArea(localContour);

                // ROI 내부 좌표 기준 bounding rect
                Rect localRect = Cv2.BoundingRect(localContour);

                // contour를 원본 좌표로 변환
                Point[] globalContour = localContour
                    .Select(p => new Point(p.X + roi.X, p.Y + roi.Y))
                    .ToArray();

                // rect를 원본 좌표로 변환
                Rect globalRect = new Rect(
                    roi.X + localRect.X,
                    roi.Y + localRect.Y,
                    localRect.Width,
                    localRect.Height);

                // contour 자체 그리기
                Cv2.DrawContours(
                    overlay,
                    new[] { globalContour },
                    contourIdx: -1,
                    color: Scalar.Yellow,
                    thickness: 1);

                // bounding rect 그리기
                Cv2.Rectangle(
                    overlay,
                    globalRect,
                    Scalar.Red,
                    1);

                // contour 번호 표시
                Point labelPoint = new Point(globalRect.X, globalRect.Y - 5);

                Cv2.PutText(
                    overlay,
                    $"{i}",
                    labelPoint,
                    HersheyFonts.HersheySimplex,
                    0.5,
                    Scalar.Cyan,
                    1);

                // area도 같이 표시
                Point areaPoint = new Point(globalRect.X, globalRect.Y + 15);

                Cv2.PutText(
                    overlay,
                    $"A:{area:F0}",
                    areaPoint,
                    HersheyFonts.HersheySimplex,
                    0.4,
                    Scalar.Cyan,
                    1);
            }

            Cv2.ImWrite(outputImagePath, overlay);
            */

            Point[] globalContour = selectedContour
            .Select(p => new Point(p.X + roi.X, p.Y + roi.Y))
            .ToArray();

            Rect localRect = Cv2.BoundingRect(selectedContour);
            Rect globalRect = new Rect(
                roi.X + localRect.X,
                roi.Y + localRect.Y,
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

            Cv2.ImWrite(outputImagePath, overlay);
        }

        public Point[]? FindBestContourByRules_ver2(
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

        public void SaveRoiBestContourDebugOverlay(
            string inputImagePath,
            string outputImagePath)
        {
            using Mat src = Cv2.ImRead(inputImagePath, ImreadModes.Color);
            if (src.Empty())
            {
                throw new Exception("이미지를 불러오지 못했습니다.");
            }

            using Mat overlay = src.Clone();
            using Mat gray = new();
            using Mat binary = new();
            using Mat morphed = new();

            Rect roi = new Rect(1000, 1230, 1200, 1302);

            using Mat roiMat = new Mat(src, roi);

            Cv2.CvtColor(roiMat, gray, ColorConversionCodes.BGR2GRAY);

            Cv2.Threshold(gray, binary, 125, 255, ThresholdTypes.Binary);

            using Mat Kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));

            Cv2.MorphologyEx(binary, morphed, MorphTypes.Open, Kernel, iterations: 1);

            Cv2.FindContours(
                morphed,
                out Point[][] contours,
                out _,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple
                );

            Point[]? selectedContour = FindBestContourByRules_ver2(
                contours,
                minArea : 1000,
                minWidth : 30,
                minHeight : 30,
                minX : 10,
                minY : 10
                );

            Cv2.Rectangle(overlay, roi, Scalar.LimeGreen, 2);

            if (selectedContour == null)
            {
                Debug.WriteLine("일치하는 contour가 없습니다.");
                Cv2.ImWrite(outputImagePath, overlay);
                return;
            }

            Point[] globalContour = selectedContour
                .Select(p => new Point(p.X + roi.X, p.Y + roi.Y))
                .ToArray();

            Rect localRect = Cv2.BoundingRect(selectedContour);

            Rect globalRect = new Rect(
                roi.X + localRect.X,
                roi.Y + localRect.Y,
                localRect.Width,
                localRect.Height);

            Cv2.DrawContours(
                overlay,
                new[] { globalContour },
                contourIdx : -1,
                color : Scalar.Yellow
                );

            Cv2.Rectangle(
                overlay,
                globalRect,
                Scalar.Red,
                thickness: 2
                );

            Cv2.PutText(
                    overlay,
                    $"Best",
                    new Point(globalRect.X, globalRect.Y + 15),
                    HersheyFonts.HersheySimplex,
                    0.4,
                    Scalar.Cyan,
                    1);

            Point[] bestContour = FindBestContourByRules_ver2(contours,
                minArea: 1000,
                minWidth: 30,
                minHeight: 30,
                minX: 10,
                minY: 10);

            bool measured = ImageMeasurementHelper.TryMeasureBoundingRectSize(
                bestContour,
                out Rect rect,
                out int widthPx,
                out int heightPx);

            if (measured)
            {
                Console.WriteLine($"Width(px): {widthPx}, Height(px): {heightPx}");
            }
            else
            {
                Console.WriteLine("측정할 contour가 없습니다.");
            }

            Cv2.ImWrite(outputImagePath, overlay);
        }
    }

    public static class ImageMeasurementHelper
    {
        public static bool TryMeasureBoundingRectSize(
            Point[] selectedContour,
            out Rect boundingRect,
            out int widthPx,
            out int heightPx)
        {
            boundingRect = default;
            widthPx = 0;
            heightPx = 0;

            if (selectedContour == null || selectedContour.Length == 0)
            {
                return false;
            }

            boundingRect = Cv2.BoundingRect(selectedContour);

            widthPx = boundingRect.Width;
            heightPx = boundingRect.Height;

            return true;
        }
    }

    public static class ImageMeasurementOverlayHelper
    {
        public static bool SaveMeasuredBoundingRectOverlay(
            Mat sourceImage,
            Point[] selectedContour,
            string savePath)
        {
            if (sourceImage == null || sourceImage.Empty())
            {
                return false;
            }

            if (selectedContour == null || selectedContour.Length == 0)
            {
                return false;
            }

            Rect rect = Cv2.BoundingRect(selectedContour);
            int widthPx = rect.Width;
            int heightPx = rect.Height;

            using Mat overlay = sourceImage.Clone();

            Cv2.Rectangle(
                overlay,
                rect,
                Scalar.LimeGreen,
                2);

            string sizeText = $"W: {widthPx}px, H: {heightPx}px";

            Point textPoint = new Point(
                rect.X,
                Math.Max(rect.Y - 10, 20));

            Cv2.PutText(
                overlay,
                sizeText,
                textPoint,
                HersheyFonts.HersheySimplex,
                0.7,
                Scalar.Yellow,
                2);

            Cv2.ImWrite(savePath, overlay);
            return true;
        }
    }
}