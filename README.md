# Camera Inspector Project

## 프로젝트 소개

Camera Inspector Project는 OpenCvSharp와 WPF를 기반으로 한 카메라 비전 검사 애플리케이션입니다.

실시간 카메라 영상을 입력받아 렌즈 왜곡 보정과 Homography 기반 좌표 보정을 적용하고, A4 기준면과 모서리 홀을 검출하여 기준값 대비 정상 여부를 판정합니다. 검사 결과는 WPF 화면에 카메라 영상, 보정 상태, 검사 상태, 측정값 형태로 표시됩니다.

주요 검사 대상은 A4 크기 기준면과 네 모서리의 홀입니다. Intrinsic calibration 데이터와 ChArUco 보드를 이용해 보정 정보를 준비한 뒤, 실시간 프레임에서 홀 위치, 직경, 원형도, 기준값 대비 오차를 확인하는 구조입니다.

## 주요 기능

- 실시간 카메라 영상 표시
- Raw, Undistorted, InspectionOverlay 표시 모드 전환
- Intrinsic calibration JSON 로드
- ChArUco 보드 기반 Homography 업데이트
- 렌즈 왜곡 보정 및 유효 ROI 계산
- A4 기준면 검출 및 정면화된 canonical frame 생성
- 네 모서리 홀 검출
- 홀 중심 좌표, 직경, 직경 오차, 위치 오차, 원형도 측정
- 기준값 대비 OK, Warning, NoObject, Error, CalibrationRequired 상태 판정
- 현재 검사 결과를 기준 홀 데이터로 저장
- JSON 설정 파일 기반 검사 조건 관리
- 이미지 전처리, threshold, contour, ROI 디버깅용 보조 기능 제공

## 기술 스택

- Language: C#
- Framework: .NET 10.0 Windows
- UI: WPF
- Computer Vision: OpenCvSharp4
- Camera Input: OpenCV VideoCapture
- Data Binding: INotifyPropertyChanged 기반 MVVM 패턴
- Configuration: JSON
- Calibration: Camera matrix, distortion coefficients, ChArUco, Homography

주요 NuGet 패키지는 다음과 같습니다.

- OpenCvSharp4
- OpenCvSharp4.runtime.win
- OpenCvSharp4.Windows

## 아키텍처 개요

프로젝트는 WPF UI, ViewModel, 검사 서비스, 보정 서비스, 모델 계층으로 나뉩니다.

```text
Camera_Insepctor_Project
├─ Views
│  └─ MainWindow.xaml / MainWindow.xaml.cs
├─ ViewModels
│  └─ MainViewModel.cs
├─ Services
│  ├─ InspectionService.cs
│  ├─ InspectionSettingsService.cs
│  └─ ImageStudyService.cs
├─ Calibration
│  ├─ CalibrationService.cs
│  ├─ CalibrationData.cs
│  ├─ FrameCorrectionService.cs
│  └─ camera_intrinsic.json
├─ Models
│  ├─ InspectionSettings.cs
│  ├─ InspectionResult.cs
│  ├─ InspectionState.cs
│  ├─ CameraDisplayMode.cs
│  ├─ HoleMeasurement.cs
│  └─ HoleReference.cs
└─ Configuration
   └─ inspection_settings.json
```

### 화면 계층

`MainWindow`는 카메라 시작, 정지, Homography 업데이트 같은 사용자 이벤트를 처리합니다. 카메라 루프는 백그라운드 작업으로 실행되며, 프레임을 읽고 검사 서비스로 전달한 뒤 UI 스레드에서 영상과 검사 결과를 갱신합니다.

### ViewModel 계층

`MainViewModel`은 UI에 표시되는 상태와 설정값을 관리합니다. 보정 상태, 검사 상태, 측정값, 마지막 갱신 시간, 입력값 검증 메시지 등을 WPF 바인딩으로 제공합니다.

또한 Intrinsic calibration 로드, Homography 갱신, 프레임 왜곡 보정, 기준 홀 데이터 저장 같은 화면과 서비스 사이의 흐름을 조율합니다.

### 검사 서비스 계층

`InspectionService`는 실제 비전 검사 로직을 담당합니다. 주요 처리 흐름은 다음과 같습니다.

1. 왜곡 보정된 프레임에서 A4 기준면 후보를 검출합니다.
2. A4 네 꼭짓점을 정렬하고 canonical frame으로 변환합니다.
3. 네 모서리 ROI에서 홀 후보를 찾습니다.
4. 홀 직경, 중심 좌표, 원형도, 위치 오차를 계산합니다.
5. 설정값과 기준 홀 데이터에 따라 정상 여부를 판정합니다.
6. 검사 결과와 overlay 표시 정보를 반환합니다.

`ImageStudyService`는 이미지 전처리와 contour 분석을 파일 기반으로 실험하거나 디버깅할 때 사용할 수 있는 보조 서비스입니다.

### 보정 계층

`CalibrationService`는 Intrinsic calibration JSON을 로드하고 ChArUco 보드 검출 결과를 이용해 Homography를 계산합니다.

`FrameCorrectionService`는 calibration 데이터를 이용해 실시간 프레임에 렌즈 왜곡 보정을 적용합니다. 필요 시 Homography 기반 perspective 보정도 수행할 수 있도록 구성되어 있습니다.

`CalibrationData`는 camera matrix, distortion coefficients, Homography, mm-per-pixel 값을 보관하고, 검사 루프에서 안전하게 사용할 수 있는 snapshot을 제공합니다.

### 모델 및 설정 계층

`Models` 폴더에는 검사 상태, 검사 결과, 검사 설정, 홀 기준값, 홀 측정값 같은 데이터 모델이 정의되어 있습니다.

`InspectionSettingsService`는 `Configuration/inspection_settings.json`을 로드하거나 기본값으로 생성하며, 기준 홀 데이터 저장 시 설정 파일을 갱신합니다.

기본 설정에는 카메라 인덱스, 요청 해상도, A4 기준 크기, 허용 오차, 왜곡 보정 alpha, Intrinsic calibration 파일 경로, 홀 직경 기준값, 홀 위치 허용 오차가 포함됩니다.

## 기본 사용 흐름

1. 애플리케이션을 실행합니다.
2. `Load Intrinsic` 버튼으로 Intrinsic calibration JSON을 로드합니다.
3. 카메라 화면에 ChArUco 보드를 위치시킨 뒤 `Update Homography`를 실행합니다.
4. 검사 대상 A4 기준면을 화면에 배치합니다.
5. 표시 모드를 `InspectionOverlay`로 전환해 실시간 검사 결과를 확인합니다.
6. 필요하면 `Save Reference`로 현재 검출된 네 개 홀을 기준값으로 저장합니다.

## 설정 파일

검사 조건은 `Configuration/inspection_settings.json`에서 관리합니다.

```json
{
  "cameraIndex": 0,
  "requestedCameraWidth": 1280,
  "requestedCameraHeight": 720,
  "expectedLongMm": 297.0,
  "expectedShortMm": 210.0,
  "toleranceMm": 0.5,
  "undistortAlpha": 0.1,
  "intrinsicCalibrationPath": "Calibration/camera_intrinsic.json",
  "expectedHoleDiameterMm": 6.0,
  "holePositionToleranceMm": 1.0,
  "holeReferences": []
}
```

## 빌드 및 실행

Windows 환경에서 .NET SDK가 설치되어 있으면 다음 명령으로 빌드할 수 있습니다.

```powershell
dotnet build
```

Visual Studio에서는 `Camera_Insepctor_Project.slnx`를 열어 WPF 애플리케이션으로 실행할 수 있습니다.
