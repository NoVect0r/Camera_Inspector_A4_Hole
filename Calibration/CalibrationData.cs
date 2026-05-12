using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Text;

/*
보정 결과를 보관하는 역할
 */

namespace Camera_Insepctor_Project.Calibration
{
    // 상속 무필요, Dispose를 사용하기 위해 IDisposable 구현
    public sealed class CalibrationData : IDisposable
    {
        private readonly object _syncLock = new();

        // Mat 객체들에 대한 해제가 완료되었는지를 확인하는 플래그
        private bool _disposed;

        // CameraMatrix는 카메라 내부 파라미터, Undistortion에 필요
        // DistCoeffs는 렌즈 왜곡 계수, Undistortion에 필요
        // Homography는 프레임의 위치, 각도 보정에 필요

        // CameraMatrix는 카메라 내부 구조 정보를 담음
        // DistCoeffs는 렌즈의 왜곡 정도 정보를 담음
        // Homography는 현재 설치 자세 보정 정보를 담음

        public Mat? CameraMatrix { get; private set; }
        public Mat? DistCoeffs { get; private set; }
        public Mat? Homography { get; private set; }

        private int _imageWidth;
        private int _imageHeight;

        public int ImageWidth
        {
            get
            {
                lock (_syncLock)
                {
                    return _imageWidth;
                }
            }
        }

        public int ImageHeight
        {
            get
            {
                lock (_syncLock)
                {
                    return _imageHeight;
                }
            }
        }

        public bool HasImageSize
        {
            get
            {
                lock (_syncLock)
                {
                    return HasImageSizeCore();
                }
            }
        }

        // Pixel 단위에서 mm 단위로 변환하기 위한 스케일 팩터
        private double _mmPerPixel = 0.25;
        public double MmPerPixel
        {
            get
            {
                lock (_syncLock)
                {
                    return _mmPerPixel;
                }
            }
            private set => _mmPerPixel = value;
        }

        // CameraMatrix 값과 DistCoeffs 값이 모두 준비되었는지 판단
        // 얘가 true라면 Undistortion이 가능하다는 뜻 (캠 자체의 base 보정)
        public bool HasIntrinsic
        {
            get
            {
                lock (_syncLock)
                {
                    return HasIntrinsicCore();
                }
            }
        }

        // Homography 값이 준비되었는지 판단
        // 얘가 true라면 캠 설치 자세의 보정이 가능
        public bool HasHomography
        {
            get
            {
                lock (_syncLock)
                {
                    return HasHomographyCore();
                }
            }
        }

        // Undistortion 보정과 Homography 보정이 모두 준비되었는지 판단
        public bool IsReadyForCorrection
        {
            get
            {
                lock (_syncLock)
                {
                    return HasIntrinsicCore() && HasHomographyCore();
                }
            }
        }

        // cameraMatrix와 distCoeffs가 비어있는지 확인, 비어있다면 예외 발생
        // 카메라 내부 보정값을 저장하는 메서드, 기존에 CalibrationData 클래스가 보유한 Mat 객체들은 Dispose를 통해 해제
        // 이렇게 하는 이유는 Clone을 통해 입력된 Mat 객체의 소유권을 CalibrationData 클래스가 가지도록 하기 위함
        public void SetIntrinsicCalibration(Mat cameraMatrix, Mat distCoeffs)
        {
            if (cameraMatrix == null || cameraMatrix.Empty())
                throw new ArgumentException("Camera matrix is empty.", nameof(cameraMatrix));

            if (distCoeffs == null || distCoeffs.Empty())
                throw new ArgumentException("Distortion coefficients are empty.", nameof(distCoeffs));

            lock (_syncLock)
            {
                EnsureNotDisposed();

                CameraMatrix?.Dispose();
                DistCoeffs?.Dispose();

                CameraMatrix = cameraMatrix.Clone();
                DistCoeffs = distCoeffs.Clone();
            }
        }

        public void SetIntrinsicImageSize(int imageWidth, int imageHeight)
        {
            if (imageWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(imageWidth), "Image width must be greater than zero.");

            if (imageHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(imageHeight), "Image height must be greater than zero.");

            lock (_syncLock)
            {
                EnsureNotDisposed();

                _imageWidth = imageWidth;
                _imageHeight = imageHeight;
            }
        }

        // Homography 행렬과 mmPerPixel 값의 유효성을 검사, 비어있다면 예외 발생
        // 설치 자세 보정값을 저장하는 메서드
        // 위와 동일하게 Dispose를 통해 받아온 Mat 객체는 해제, Clone을 통해 입력된 Mat 객체의 소유권을 CalibrationData 클래스가 가지도록 함
        public void SetHomography(Mat homography, double mmPerPixel)
        {
            if (homography == null || homography.Empty())
                throw new ArgumentException("Homography is empty.", nameof(homography));

            if (homography.Rows != 3 || homography.Cols != 3)
                throw new ArgumentException("Homography must be a 3x3 matrix.", nameof(homography));

            if (mmPerPixel <= 0)
                throw new ArgumentOutOfRangeException(nameof(mmPerPixel), "MmPerPixel must be greater than zero.");

            lock (_syncLock)
            {
                EnsureNotDisposed();

                Homography?.Dispose();

                Homography = homography.Clone();
                MmPerPixel = mmPerPixel;
            }
        }

        // 얘는 카메라 설치 자세 보정값을 초기화하는 메서드, Dispose와는 개념 자체가 다름
        // Homography, mmPixel 값을 초기화하여 설치 자세 보정을 다시 진행할 수 있도록 함
        public void ClearHomography()
        {
            lock (_syncLock)
            {
                EnsureNotDisposed();

                Homography?.Dispose();
                Homography = null;
            }
        }

        public bool TryCreateIntrinsicSnapshot(out Mat? cameraMatrix, out Mat? distCoeffs)
        {
            lock (_syncLock)
            {
                EnsureNotDisposed();

                if (!HasIntrinsicCore())
                {
                    cameraMatrix = null;
                    distCoeffs = null;
                    return false;
                }

                cameraMatrix = CameraMatrix!.Clone();
                distCoeffs = DistCoeffs!.Clone();
                return true;
            }
        }

        public bool TryGetIntrinsicImageSize(out int imageWidth, out int imageHeight)
        {
            lock (_syncLock)
            {
                EnsureNotDisposed();

                if (!HasImageSizeCore())
                {
                    imageWidth = 0;
                    imageHeight = 0;
                    return false;
                }

                imageWidth = _imageWidth;
                imageHeight = _imageHeight;
                return true;
            }
        }

        public bool TryCreateHomographySnapshot(out Mat? homography, out double mmPerPixel)
        {
            lock (_syncLock)
            {
                EnsureNotDisposed();

                if (!HasHomographyCore())
                {
                    homography = null;
                    mmPerPixel = 0;
                    return false;
                }

                homography = Homography!.Clone();
                mmPerPixel = _mmPerPixel;
                return true;
            }
        }

        public bool TryCreateCorrectionSnapshot(
            out Mat? cameraMatrix,
            out Mat? distCoeffs,
            out Mat? homography,
            out double mmPerPixel)
        {
            lock (_syncLock)
            {
                EnsureNotDisposed();

                if (!HasIntrinsicCore() || !HasHomographyCore())
                {
                    cameraMatrix = null;
                    distCoeffs = null;
                    homography = null;
                    mmPerPixel = 0;
                    return false;
                }

                cameraMatrix = CameraMatrix!.Clone();
                distCoeffs = DistCoeffs!.Clone();
                homography = Homography!.Clone();
                mmPerPixel = _mmPerPixel;
                return true;
            }
        }

        // Dispose 패턴 구현, CalibrationData Class가 보유한 Mat 객체들을 해제
        public void Dispose()
        {
            lock (_syncLock)
            {
                if (_disposed)
                    return;

                CameraMatrix?.Dispose();
                DistCoeffs?.Dispose();
                Homography?.Dispose();

                CameraMatrix = null;
                DistCoeffs = null;
                Homography = null;

                _disposed = true;
            }
        }

        // 외부로부터 받아온 Mat 객체의 사용 전, Mat 객체가 이미 Dispose 되었는지 확인하는 메서드
        // 이미 Dispose된 객체에 접근하려는 시도를 예외 처리를 통해 방지
        private void EnsureNotDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CalibrationData));
        }

        private bool HasIntrinsicCore()
        {
            return CameraMatrix != null &&
                DistCoeffs != null &&
                !CameraMatrix.Empty() &&
                !DistCoeffs.Empty();
        }

        private bool HasHomographyCore()
        {
            return Homography != null &&
                !Homography.Empty();
        }

        private bool HasImageSizeCore()
        {
            return _imageWidth > 0 &&
                _imageHeight > 0;
        }
    }
}
