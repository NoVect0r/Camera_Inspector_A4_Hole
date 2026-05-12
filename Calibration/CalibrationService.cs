using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCvSharp.Aruco;

/*
 -ChArUco Board 검출
CameraMatrix, distCoeffs 계산
CameraMatrix -> 카메라 내부 파라미터
distCoeffs -> 렌즈 왜곡 계수
homography 계산
보정 데이터 저장
보정 데이터 불러오기

CameraMatrix, distCoeffs는 카메라 자체 보정이기 때문에 자주 계산 요구 x
homography는 매 프로그램 실행시마다 위치, 각도 등이 변하기 때문에 계산 필요
 */

namespace Camera_Insepctor_Project.Calibration
{
    public class CalibrationService
    {
        // ChArUco Board에 대한 기본 스펙
        private const int SquaresX = 10;
        private const int SquaresY = 7;
        private const float SquareLengthMm = 22.0f;
        private const float MarkerLengthMm = 16.0f;
        private const PredefinedDictionaryType DictionaryType = PredefinedDictionaryType.Dict4X4_50;

        // ChArUco Board의 사이즈 (300, 210)
        private const double BoardWidthMm = 300.0;
        private const double BoardHeightMm = 210.0;

        private const double CheckerAreaWidthMm = SquaresX * SquareLengthMm;   // 220mm
        private const double CheckerAreaHeightMm = SquaresY * SquareLengthMm;  // 154mm

        private const double MarginXmm = (BoardWidthMm - CheckerAreaWidthMm) / 2.0;   // 40mm
        private const double MarginYmm = (BoardHeightMm - CheckerAreaHeightMm) / 2.0; // 28mm

        // 출력 과정에서의 Pixel to Mm 계산
        // 근데 이건 임의로 세팅하는거임 아니면 다른 calibration을 통해 측정하는거임?
        public const double PixelsPerMm = 4.0;
        public const double MmPerPixel = 1.0 / PixelsPerMm;

        public static readonly Size WarpedSize = new(
            (int)(BoardWidthMm * PixelsPerMm),    // 1200px
            (int)(BoardHeightMm * PixelsPerMm));  // 840px


        public CalibrationData LoadIntrinsicFromJson(string jsonPath)
        {
            // 아래 두개는 jsonPath의 File 유효성 검증
            if (string.IsNullOrWhiteSpace(jsonPath))
                throw new ArgumentException("JSON path is empty.", nameof(jsonPath));

            if (!File.Exists(jsonPath))
                throw new FileNotFoundException("Intrinsic calibration JSON file was not found.", jsonPath);

            string json = File.ReadAllText(jsonPath);

            // json 문자열을 읽어서 IntrinsicCalibrationFile 타입의 C# 객체로 바꾸는 과정
            var fileData = JsonSerializer.Deserialize<IntrinsicCalibrationFile>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            // Desirizalize를 완료한 후 파일의 유효성 검증
            if (fileData == null)
                throw new InvalidOperationException("Failed to deserialize intrinsic calibration JSON.");

            ValidateIntrinsicFile(fileData);

            using var cameraMatrix = CreateCameraMatrix(fileData.CameraMatrix!);
            using var distCoeffs = CreateDistCoeffs(fileData.DistCoeffs!);

            var calibrationData = new CalibrationData();

            // CalibrationData의 SetIntrinsicCalibration 메서드를 불러옴. 여기선 cameraMatrix, distCoeffs를 내부로 전달
            calibrationData.SetIntrinsicCalibration(cameraMatrix, distCoeffs);
            calibrationData.SetIntrinsicImageSize(fileData.ImageWidth, fileData.ImageHeight);

            // calibratoinData.SetIntrinsicCalibration을 했기 때문에 calibrationData는 비어있지 않음
            // 메서드의 결과물을 바깥에서 받아야 하기 때문에 return이 필요
            return calibrationData;
        }

        // 전처리 완료된 fileData가 유효한지 검증하는 메서드
        private static void ValidateIntrinsicFile(IntrinsicCalibrationFile fileData)
        {
            // Json File에 CameraMatrix라는 항목이 있는지
            if (fileData.CameraMatrix == null)
                throw new InvalidOperationException("CameraMatrix is missing.");

            // 3x3의 행렬이여야 하는데, rows를 먼저 검사. 3이 아니면 예외 발생
            if (fileData.CameraMatrix.Length != 3)
                throw new InvalidOperationException("CameraMatrix must have 3 rows.");

            // 0 ~ 2까지 루프를 돌며 각 row마다 값이 존재하는지 확인. 마찬가지로 3x3임을 검증
            for (int row = 0; row < 3; row++)
            {
                if (fileData.CameraMatrix[row] == null || fileData.CameraMatrix[row].Length != 3)
                    throw new InvalidOperationException("CameraMatrix must be a 3x3 matrix.");
            }

            // Json File에 DistCoeffs라는 항목이 있는지, 그리고 이 값이 유효한지 검증
            if (fileData.DistCoeffs == null || fileData.DistCoeffs.Length == 0)
                throw new InvalidOperationException("DistCoeffs is missing.");

            if (fileData.ImageWidth <= 0)
                throw new InvalidOperationException("ImageWidth must be greater than zero.");

            if (fileData.ImageHeight <= 0)
                throw new InvalidOperationException("ImageHeight must be greater than zero.");
        }

        private static Mat CreateCameraMatrix(double[][] values)
        {
            // 3x3 크기의 새로운 Mat 객체 생성
            var mat = new Mat(3, 3, MatType.CV_64FC1);

            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    // 이건 mat에 값을 넣는건가? 잘 모르겠다
                    mat.Set(row, col, values[row][col]);
                }
            }

            return mat;
        }

        private static Mat CreateDistCoeffs(double[] values)
        {
            // 1 x values.Length 크기인 새로운 Mat 객체 생성
            var mat = new Mat(1, values.Length, MatType.CV_64FC1);

            for (int i = 0; i < values.Length; i++)
            {
                // 얘도 mat에 값 넣는거같은데 잘 모르겠네
                mat.Set(0, i, values[i]);
            }

            return mat;
        }

        private class IntrinsicCalibrationFile
        {
            [JsonPropertyName("imageWidth")]
            public int ImageWidth { get; set; }

            [JsonPropertyName("imageHeight")]
            public int ImageHeight { get; set; }

            // Json의 PropertyName을 설정하고, 이 이름과 동일한 값이 있다면 받아오기?
            [JsonPropertyName("cameraMatrix")]
            public double[][]? CameraMatrix { get; set; }

            [JsonPropertyName("distCoeffs")]
            public double[]? DistCoeffs { get; set; }
        }

        // WPF에서 Homography 계산 버튼을 눌렀을 때 실행할 메서드
        // sourceFrame은 1 frame image, calibrationData는 CameraMatrix, DistCoeffs를 들고 있는 객체
        // messag를 통해 성공 실패 여부 반환, 실패 시 실패의 이유를 반환함으로서 실패 사유 알림
        public bool TryUpdateHomography(
            Mat sourceFrame,
            CalibrationData calibrationData,
            out string message)
        {
            if (sourceFrame == null || sourceFrame.Empty())
                throw new ArgumentException("Source frame is empty.", nameof(sourceFrame));

            if (calibrationData == null)
                throw new ArgumentNullException(nameof(calibrationData));

            if (!calibrationData.TryCreateIntrinsicSnapshot(
                out Mat? cameraMatrix,
                out Mat? distCoeffs))
                throw new InvalidOperationException("Intrinsic calibration data is not ready.");

            // undistorted image를 구하고 이 이미지에 Homography를 적용하는 식
            using var undistorted = new Mat();
            using (cameraMatrix)
            using (distCoeffs)
            {
                Cv2.Undistort(
                    sourceFrame,
                    undistorted,
                    cameraMatrix!,
                    distCoeffs!
                );
            }

            // 아래에 있는 DetectCharucoCorners 사용, ChArUco corner 찾기
            CharucoDetection detection = DetectCharucoCorners(undistorted);

            // 여기서 검출된 ChArUco corner를 이용해 Homography 계산 시도
            if (!TryBuildHomography(detection, out Mat? homography, out message))
            {
                homography?.Dispose();
                return false;
            }

            // 위 if문에서 문제 없이 내려왔다면 calibrationData.SetHomography를 통해 Homography 저장
            // using문이 끝날 때 homography를 정리하는 것인데, 어차피 SetHomography 내에서 Clone 진행하기 때문에 정리해도 됨
            using (homography)
            {
                calibrationData.SetHomography(homography!, MmPerPixel);
            }

            message =
                "Homography updated. " +
                message;

            return true;
        }

        // Undistorted frame에서 ChArUco Corner를 검출, 이후 CharucoDetection 객체로 묶어서 반환 진행
        private static CharucoDetection DetectCharucoCorners(Mat undistorted)
        {
            // 입력받은 undistorted 완료된 Mat 객체를 grayscale로 변환
            using var gray = ConvertToGray(undistorted);

            // ChArUco 검출 함수 사용

            // markerIds -> ArUco marker의 Index
            // makerCorners -> 각 ArUco marker Index에 대한 네 모서리 좌표

            // ChArUco corner -> 체스판 격자의 내부 교차점
            // charucoIds[i]는 charucoCorners[i]가 몇번 ChArUco corner인지 알려줌
            // i는 검출 결과 배열에서의 순서이며, charucoIds[i]가 실제 보드 위의 corner index

            // 위 5개 (SquaresX ~ DictionaryType)는 ChArUco Board의 스펙을 나타냄
            CvAruco.DetectCharucoBoard(
                gray,
                SquaresX,
                SquaresY,
                SquareLengthMm,
                MarkerLengthMm,
                DictionaryType,
                out Point2f[] charucoCorners,
                out int[] charucoIds,
                out Point2f[][] markerCorners,
                out int[] markerIds);

            return new CharucoDetection(
                charucoCorners,
                charucoIds,
                markerCorners,
                markerIds);
        }

        // 검출된 corner 값을 이용하여 Homography 행렬을 계산하는 메서드
        // 직전에 생성한 CharucoDetection을 받아옴, 이후 Homography 행렬을 계산
        // 이 메서드의 성공 여부에 따라 homography는 반환될수도, 안 될 수도 있음, message는 항상 반환
        private static bool TryBuildHomography(
            CharucoDetection detection,
            out Mat? homography,
            out string message
            )
        {
            homography = null;

            // detection의 Corner 갯수 유효성 검증
            if (detection.CharucoCorners.Length < 12)
            {
                message = $"Not enough ChArUco corners. Current={detection.CharucoCorners.Length}, Required>=12";
                return false;
            }

            // srcPoints는 현재 카메라 이미지에서 찾은 corner 좌표
            // ex. 카메라 이미지 안에서 어떤 corner가 x = 256, y = 128에 보임 등

            // dstPoints는 정면화된 (Perspective) 출력 이미지에서 해당 corner가 가야할 목표 좌표
            // ex. 실제 보드에서 그 corner가 x = 60, y = 50일 때
            // PixelsPerMm이 4이기 때문에 목표 좌표는 x = 240, y = 200.
            
            // srcPoints에 있는 여러 이미지 좌표가 각각 대응되는 dstPoints 좌표로 이동하도록 만드는 3x3 perspective 변환 행렬이 Homography
            var srcPoints = new List<Point2f>();
            var dstPoints = new List<Point2f>();

            for (int i = 0; i < detection.CharucoIds.Length; i++)
            {
                // 검출된 ChArUco corner의 ID를 가져옴
                int id = detection.CharucoIds[i];

                // 이 ID가 실제 보드 위에서 몇 mm 위치인지 계산
                // xMm, yMm은 이 CharucoIds[i]의 x, y 위치를 의미
                if (!TryGetCharucoCornerPositionMm(id, out double xMm, out double yMm))
                    continue;

                // ID에 해당하는 corner가 이미지 안에서 어디에 보였는지를 imagePoint에 저장
                Point2f imagePoint = detection.CharucoCorners[i];

                // 실제 mm 좌표를 warped imgae의 pixel 좌표로 바꿈
                // ex. xMm = 80mm, PixelsPerMm = 4라면 dstX = 320
                float dstX = (float)(xMm * PixelsPerMm);
                float dstY = (float)(yMm * PixelsPerMm);

                // srcPoints[i] -> 현재 이미지에서 보인 위치
                // dstPoints[i] -> 정면 이미지에서 가야 할 위치
                srcPoints.Add(imagePoint);
                dstPoints.Add(new Point2f(dstX, dstY));
            }

            // Calibration을 위한 쌍 갯수 검증
            if (srcPoints.Count < 12)
            {
                message = $"Not enough valid point pairs. Current={srcPoints.Count}, Required>=12";
                return false;
            }

            // Point2f 형식을 InputArry 형식으로 변환, using을 사용했기 때문에 이 메서드가 끝나면 자동 해제
            using InputArray srcInput = InputArray.Create(srcPoints.ToArray());
            using InputArray dstInput = InputArray.Create(dstPoints.ToArray());

            // Homography 계산 진행
            // srcInput에 있는 점들이 dstInput에 있는 점으로 가도록 만드는 변환 행렬을 구함
            homography = Cv2.FindHomography(
                srcInput,
                dstInput,
                HomographyMethods.Ransac,
                3.0);

            // 완성된 homography의 유효성 검증
            if (homography is null || homography.Empty())
            {
                message = "Cv2.FindHomography returned empty matrix.";
                return false;
            }

            message =
                $"PointPairs={srcPoints.Count}, " +
                $"Output={WarpedSize.Width}x{WarpedSize.Height}, " +
                $"PixelsPerMm={PixelsPerMm}, MmPerPixel={MmPerPixel}";

            return true;
        }

        // ChArUco Board의 corner ID를 실제 mm 계산을 위한 좌표로 바꾸는 메서드
        // charucold corenr가 실제 보드에서 x는 몇 mm, y는 몇 mm에 있는지를 계산
        private static bool TryGetCharucoCornerPositionMm(
            int charucoId,
            out double xMm,
            out double yMm
            )
        {
            // 내 Board는 SquaresX = 10, SquaresY = 7임
            // 근데 ChArUco corner는 칸과 칸 사이의 내부 교차점을 의미함
            int innerCornersX = SquaresX - 1;
            int innerCornersY = SquaresY - 1;

            /*
            row 0:  0   1   2   3   4   5   6   7   8
            row 1:  9  10  11  12  13  14  15  16  17
            row 2: 18  19  20  21  22  23  24  25  26
            row 3: 27  28  29  30  31  32  33  34  35
            row 4: 36  37  38  39  40  41  42  43  44
            row 5: 45  46  47  48  49  50  51  52  53
            이런식이기 때문에 ID를 보고 row, col을 계산해야 함. 백준에서 해봤던거
             */
            int col = charucoId % innerCornersX;
            int row = charucoId / innerCornersX;

            // col, row에 대한 유효성 검증
            if (col < 0 || col >= innerCornersX || row < 0 || row >= innerCornersY)
            {
                xMm = 0;
                yMm = 0;
                return false;
            }

            // 전체 target의 크기는 300 x 210 (Board 제작시 setting값)
            // Checker 영역은 220 x 154 (22mm * 10, 22m * 7)
            // 즉 좌우 margin은 (300 - 220) / 2 = 40
            // 상하 maring은 (210 - 154) / 2 = 28
            // 전체 종이를 기준으로 첫 checker 좌표는 x = 40, y = 28
            // 그런데 ChArUco Marker는 4개의 사각형의 교점을 기준으로 잡기 때문에 각 좌표에 22를 더함
            xMm = MarginXmm + ((col + 1) * SquareLengthMm);
            yMm = MarginYmm + ((row + 1) * SquareLengthMm);

            return true;
        }

        // ChArUco Board 검출은 보통 grayscale 이미지에서 처리하기 때문에 전처리
        // 이미 grayscale일 시 clone해서 반환, 아니라면 gray로 변환해서 반환
        // 항상 호출한 쪽에서 Dispose할 수 있도록 하기 위함
        private static Mat ConvertToGray(Mat frame)
        {
            if (frame.Channels() == 1)
                return frame.Clone();

            var gray = new Mat();
            Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
            return gray;
        }

        // ChArUco 검출 결과값 저장을 위한 class
        private sealed class CharucoDetection
        {
            public Point2f[] CharucoCorners { get; }
            public int[] CharucoIds { get; }

            public Point2f[][] MarkerCorners { get; }
            public int[] MarkerIds { get; }

            public CharucoDetection(
                Point2f[] charucoCorners,
                int[] charucoIds,
                Point2f[][] markerCorners,
                int[] markerIds)
            {
                CharucoCorners = charucoCorners;
                CharucoIds = charucoIds;
                MarkerCorners = markerCorners;
                MarkerIds = markerIds;
            }
        }
    }
}
