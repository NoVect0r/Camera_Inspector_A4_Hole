using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Text;

/*
CalibrationService에서 만들어진 CameraMatrix, distCoeffs, homography를 사용하여
매 frame마다 Undistortion + WarpPerspective 적용
Undistortion -> 렌즈 왜곡 보정
WarpPerspective -> homography를 이용하여 프레임의 위치, 각도 보정

여기선 생성된 보정 데이터를 실시간 frame에 적용하는 것이라 생각하면 됨
 */

namespace Camera_Insepctor_Project.Calibration
{
    public class FrameCorrectionService
    {
        private readonly Size _outputSize;

        public FrameCorrectionService()
        {
            // 콘솔 검증에서 사용한 기준
            // 300mm x 210mm 영역을 1200px x 840px로 변환
            // PixelsPerMm = 4
            // MmPerPixel = 0.25
            _outputSize = CalibrationService.WarpedSize;
        }

        // 출력 크기를 지정하는 생성자 정의
        public FrameCorrectionService(Size outputSize)
        {
            if (outputSize.Width <= 0 || outputSize.Height <= 0)
                throw new ArgumentException("Output size must be greater than zero.", nameof(outputSize));

            _outputSize = outputSize;
        }

        // Undistort + WarpPerspective를 진행, corrected는 외부에서 사용해야 하기 때문에 추후 Dispose 필요
        public Mat Correct(Mat sourceFrame, CalibrationData calibrationData)
        {
            if (sourceFrame == null || sourceFrame.Empty())
                throw new ArgumentException("Source frame is empty.", nameof(sourceFrame));

            // 이건 calibrationData 객체 자체를 받았는지에 대한 검사
            if (calibrationData == null)
                throw new ArgumentNullException(nameof(calibrationData));

            if (!calibrationData.TryCreateCorrectionSnapshot(
                out Mat? cameraMatrix,
                out Mat? distCoeffs,
                out Mat? homography,
                out _))
                throw new InvalidOperationException("Calibration data is not ready for correction.");

            using var undistorted = new Mat();
            using (cameraMatrix)
            using (distCoeffs)
            using (homography)
            {
                // !는 컴파일러에게 "앞에서 검사했으니 여기서는 null이 아니라고 간주해도 된다"고 알려주는 표시
                // Undistort로 렌즈 왜곡 보정 진행
                Cv2.Undistort(
                    sourceFrame,
                    undistorted,
                    cameraMatrix!,
                    distCoeffs!
                );

                var corrected = new Mat();

                // WarpPerspective로 시점 보정 후 corrected 반환
                Cv2.WarpPerspective(
                    undistorted,
                    corrected,
                    homography!,
                    _outputSize
                );

                return corrected;
            }
        }

        // Undistort를 진행, undistorted는 외부에서 사용해야 하기 때문에 추후 Dispose 필요
        // 이 메서드는 추후 Undistort와 WarpPerspective 사이에서 문제 발생 시 디버깅을 위한 메서드
        public Mat UndistortOnly(Mat sourceFrame, CalibrationData calibrationData)
        {
            if (sourceFrame == null || sourceFrame.Empty())
                throw new ArgumentException("Source frame is empty.", nameof(sourceFrame));

            // 이건 calibrationData 객체 자체를 받았는지에 대한 검사
            if (calibrationData == null)
                throw new ArgumentNullException(nameof(calibrationData));

            if (!calibrationData.TryCreateIntrinsicSnapshot(
                out Mat? cameraMatrix,
                out Mat? distCoeffs))
                throw new InvalidOperationException("Intrinsic calibration data is not ready.");

            var undistorted = new Mat();
            using (cameraMatrix)
            using (distCoeffs)
            {
                // !는 컴파일러에게 "앞에서 검사했으니 여기서는 null이 아니라고 간주해도 된다"고 알려주는 표시
                // Undistort로 렌즈 왜곡 보정 진행
                Cv2.Undistort(
                    sourceFrame,
                    undistorted,
                    cameraMatrix!,
                    distCoeffs!
                );
            }

            return undistorted;
        }

        public Mat UndistortOnlyWithOptimalNewCameraMatrix(
            Mat sourceFrame,
            CalibrationData calibrationData,
            double alpha,
            out Rect validPixelRoi)
        {
            if (sourceFrame == null || sourceFrame.Empty())
                throw new ArgumentException("Source frame is empty.", nameof(sourceFrame));

            if (calibrationData == null)
                throw new ArgumentNullException(nameof(calibrationData));

            if (alpha < 0 || alpha > 1)
                throw new ArgumentOutOfRangeException(nameof(alpha), "Alpha must be between 0 and 1.");

            if (!calibrationData.TryCreateIntrinsicSnapshot(
                out Mat? cameraMatrix,
                out Mat? distCoeffs))
                throw new InvalidOperationException("Intrinsic calibration data is not ready.");

            var undistorted = new Mat();
            using (cameraMatrix)
            using (distCoeffs)
            using (Mat newCameraMatrix = Cv2.GetOptimalNewCameraMatrix(
                cameraMatrix!,
                distCoeffs!,
                sourceFrame.Size(),
                alpha,
                sourceFrame.Size(),
                out validPixelRoi))
            {
                Cv2.Undistort(
                    sourceFrame,
                    undistorted,
                    cameraMatrix!,
                    distCoeffs!,
                    newCameraMatrix);
            }

            return undistorted;
        }
    }
}
