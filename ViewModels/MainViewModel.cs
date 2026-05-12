using Camera_Insepctor_Project.Calibration;
using Camera_Insepctor_Project.Commands;
using Camera_Insepctor_Project.Models;
using Camera_Insepctor_Project.Services;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Controls;

/*
InspectionResult를 받아서 WPF 갱신

StatusText
WarningMessage
MeasuredValueText
IsRunning
LastUpdatedTimeText
등등을 받아서 WPF에 갱신하는 역할 담당
 */

namespace Camera_Insepctor_Project.ViewModels
{
    internal class MainViewModel : INotifyPropertyChanged
    {
        public MainViewModel()
            : this(new InspectionSettings(), string.Empty)
        {
        }

        public MainViewModel(InspectionSettings settings, string settingsPath)
        {
            // CalibrationService, FrameCorrectionService 초기화 및 생성
            _calibrationService = new CalibrationService();
            _frameCorrectionService = new FrameCorrectionService();
            _settings = settings;
            _settingsPath = settingsPath;

            ApplyInitialSettings(settings);

            // LoadIntrinsicCalibrationCommand 가 실행되면 LoadIntrinsicCalibration이 실행되도록 바인딩
            LoadIntrinsicCalibrationCommand = new RelayCommand(LoadIntrinsicCalibration);
            SaveReferenceCommand = new RelayCommand(SaveReferenceFromLatestResult);
        }

        // 최신 검사 결과 전체를 보관하는 원본 데이터
        private InspectionResult? _latestInspectionResult;

        // 상태 비교용, 이전 검사의 판정 상태
        private InspectionState _previousDecisionState;

        // 상태 비교용, 최신 검사의 판정 상태
        private InspectionState _currentDecisionState;

        // UI에 표시된 마지막 검사 상태 (화면 표시상태 추적용)
        private InspectionState _lastDisplayedState;

        // Property 값이 변할 경우 EventHandler를 통해 UI에 알림 전달
        public event PropertyChangedEventHandler? PropertyChanged;

        // Calibration JSON File Load, Homography 계산 담당
        private readonly CalibrationService _calibrationService;

        // 매 frame마다 Undistort + WarpPerspective 적용 담당
        private readonly FrameCorrectionService _frameCorrectionService;

        private readonly InspectionSettings _settings;
        private readonly string _settingsPath;
        private readonly InspectionSettingsService _settingsService = new();

        // 현재 들고 있는 보정 데이터
        private readonly object _calibrationDataLock = new();
        private CalibrationData? _calibrationData;

        // JSON File의 경로 지정
        private string _intrinsicJsonPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Calibration", "camera_intrinsic.json");

        public IReadOnlyList<CameraDisplayMode> DisplayModes { get; } =
            new[]
            {
                CameraDisplayMode.Raw,
                CameraDisplayMode.Undistorted,
                CameraDisplayMode.InspectionOverlay
            };

        private CameraDisplayMode _selectedDisplayMode = CameraDisplayMode.Raw;
        public CameraDisplayMode SelectedDisplayMode
        {
            get => _selectedDisplayMode;
            set
            {
                if (_selectedDisplayMode == value) return;
                _selectedDisplayMode = value;
                OnPropertyChanged(nameof(SelectedDisplayMode));
                OnPropertyChanged(nameof(IsInspectionOverlaySelected));

                if (!IsInspectionOverlaySelected)
                {
                    ClearInspectionDisplayForPreviewMode();
                }
            }
        }

        public bool IsInspectionOverlaySelected =>
            SelectedDisplayMode == CameraDisplayMode.InspectionOverlay;

        private readonly object _validPixelRoiLock = new();
        private Rect _latestValidPixelRoi;
        private bool _hasLatestValidPixelRoi;

        private double _undistortAlpha = 0.1;
        private string _undistortAlphaText = "0.1";
        private string _lastUndistortStatusMessage = string.Empty;
        public double UndistortAlpha
        {
            get => _undistortAlpha;
            set
            {
                double clampedValue = Math.Clamp(value, 0, 1);

                if (Math.Abs(_undistortAlpha - clampedValue) < 0.0001) return;
                _undistortAlpha = clampedValue;
                _undistortAlphaText = FormatNumber(clampedValue);
                OnPropertyChanged(nameof(UndistortAlpha));
                OnPropertyChanged(nameof(UndistortAlphaText));
            }
        }

        public string UndistortAlphaText
        {
            get => _undistortAlphaText;
            set
            {
                if (_undistortAlphaText == value) return;
                _undistortAlphaText = value;
                OnPropertyChanged(nameof(UndistortAlphaText));

                if (!TryParseNumber(value, out double parsedValue))
                {
                    InputValidationMessage = "Undistort alpha는 숫자로 입력해야 합니다.";
                    return;
                }

                if (parsedValue < 0 || parsedValue > 1)
                {
                    InputValidationMessage = "Undistort alpha는 0부터 1 사이여야 합니다.";
                    return;
                }

                if (Math.Abs(_undistortAlpha - parsedValue) >= 0.0001)
                {
                    _undistortAlpha = parsedValue;
                    OnPropertyChanged(nameof(UndistortAlpha));
                }

                ClearInputValidationMessage();
            }
        }

        // _statusText 필드와 StatusText Property 생성
        private string _statusText = "준비";
        public string StatusText
        {
            get => _statusText;
            set
            {
                // 입력된 값이 기존 _statusText와 동일하면 변경 x
                if (_statusText == value) return;
                _statusText = value;
                // OnPropertyChanged 메서드 호출로 UI에 값 변경 알림
                OnPropertyChanged(nameof(StatusText));
            }
        }

        // 보정 상태 표시용 텍스트
        // Intrinsic load, Homography update 같은 calibration 관련 메시지는 여기에 표시
        private string _calibrationStatusText = "보정 대기";
        public string CalibrationStatusText
        {
            get => _calibrationStatusText;
            set
            {
                if (_calibrationStatusText == value) return;
                _calibrationStatusText = value;
                OnPropertyChanged(nameof(CalibrationStatusText));
            }
        }

        // _WarningMessage 필드와 WarningMessage Property 생성
        private string _warningMessage = string.Empty;
        public string WarningMessage
        {
            get => _warningMessage;
            set
            {
                // 입력된 값이 기존 _warningMessage와 동일하면 변경 x
                if (_warningMessage == value) return;
                _warningMessage = value;
                // OnPropertyChanged 메서드 호출로 UI에 값 변경 알림
                OnPropertyChanged(nameof(WarningMessage));
            }
        }

        private string _inputValidationMessage = string.Empty;
        public string InputValidationMessage
        {
            get => _inputValidationMessage;
            set
            {
                if (_inputValidationMessage == value) return;
                _inputValidationMessage = value;
                OnPropertyChanged(nameof(InputValidationMessage));
            }
        }

        // 프로그램 실행 상태
        private bool _isRunning = false;
        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (_isRunning == value) return;
                _isRunning = value;
                OnPropertyChanged(nameof(IsRunning));
            }
        }

        // 측정값 텍스트, 초기값은 "-"로 설정
        private string _measuredValueText = "-";
        public string MeasuredValueText
        {
            get => _measuredValueText;
            set
            {
                if (_measuredValueText == value) return;
                _measuredValueText = value;
                OnPropertyChanged(nameof(MeasuredValueText));
            }
        }

        // 마지막 업데이트 시간 텍스트, 초기값은 "-"로 설정
        private string _lastUpdatedTimeText = "-";
        public string LastUpdatedTimeText
        {
            get => _lastUpdatedTimeText;
            set
            {
                if (_lastUpdatedTimeText == value) return;
                _lastUpdatedTimeText = value;
                OnPropertyChanged(nameof(LastUpdatedTimeText));
            }
        }

        // 검사 기준 긴 면(mm)
        // 나중에 사용자가 UI에서 입력할 값
        private double _expectedLongMm = 297.0;
        private string _expectedLongMmText = "297.0";
        public double ExpectedLongMm
        {
            get => _expectedLongMm;
            set
            {
                if (!IsValidPositiveDimension(value)) return;
                if (value < _expectedShortMm) return;
                if (Math.Abs(_expectedLongMm - value) < 0.0001) return;
                _expectedLongMm = value;
                _expectedLongMmText = FormatNumber(value);
                OnPropertyChanged(nameof(ExpectedLongMm));
                OnPropertyChanged(nameof(ExpectedLongMmText));
            }
        }

        public string ExpectedLongMmText
        {
            get => _expectedLongMmText;
            set
            {
                if (_expectedLongMmText == value) return;
                _expectedLongMmText = value;
                OnPropertyChanged(nameof(ExpectedLongMmText));

                if (!TryParseNumber(value, out double parsedValue))
                {
                    InputValidationMessage = "긴 면은 숫자로 입력해야 합니다.";
                    return;
                }

                if (!IsValidPositiveDimension(parsedValue))
                {
                    InputValidationMessage = "긴 면은 0보다 커야 합니다.";
                    return;
                }

                if (parsedValue < _expectedShortMm)
                {
                    InputValidationMessage = "긴 면은 짧은 면보다 크거나 같아야 합니다.";
                    return;
                }

                if (Math.Abs(_expectedLongMm - parsedValue) >= 0.0001)
                {
                    _expectedLongMm = parsedValue;
                    OnPropertyChanged(nameof(ExpectedLongMm));
                }

                ClearInputValidationMessage();
            }
        }

        // 검사 기준 짧은 면(mm)
        private double _expectedShortMm = 210.0;
        private string _expectedShortMmText = "210.0";
        public double ExpectedShortMm
        {
            get => _expectedShortMm;
            set
            {
                if (!IsValidPositiveDimension(value)) return;
                if (value > _expectedLongMm) return;
                if (Math.Abs(_expectedShortMm - value) < 0.0001) return;
                _expectedShortMm = value;
                _expectedShortMmText = FormatNumber(value);
                OnPropertyChanged(nameof(ExpectedShortMm));
                OnPropertyChanged(nameof(ExpectedShortMmText));
            }
        }

        public string ExpectedShortMmText
        {
            get => _expectedShortMmText;
            set
            {
                if (_expectedShortMmText == value) return;
                _expectedShortMmText = value;
                OnPropertyChanged(nameof(ExpectedShortMmText));

                if (!TryParseNumber(value, out double parsedValue))
                {
                    InputValidationMessage = "짧은 면은 숫자로 입력해야 합니다.";
                    return;
                }

                if (!IsValidPositiveDimension(parsedValue))
                {
                    InputValidationMessage = "짧은 면은 0보다 커야 합니다.";
                    return;
                }

                if (parsedValue > _expectedLongMm)
                {
                    InputValidationMessage = "짧은 면은 긴 면보다 작거나 같아야 합니다.";
                    return;
                }

                if (Math.Abs(_expectedShortMm - parsedValue) >= 0.0001)
                {
                    _expectedShortMm = parsedValue;
                    OnPropertyChanged(nameof(ExpectedShortMm));
                }

                ClearInputValidationMessage();
            }
        }

        // 허용 오차(mm)
        // 예: 5.0이면 기준값 ±5mm까지 허용
        private double _toleranceMm = 10.0;
        private string _toleranceMmText = "10.0";
        public double ToleranceMm
        {
            get => _toleranceMm;
            set
            {
                if (value < 0 || double.IsNaN(value) || double.IsInfinity(value)) return;
                if (Math.Abs(_toleranceMm - value) < 0.0001) return;
                _toleranceMm = value;
                _toleranceMmText = FormatNumber(value);
                OnPropertyChanged(nameof(ToleranceMm));
                OnPropertyChanged(nameof(ToleranceMmText));
            }
        }

        public string ToleranceMmText
        {
            get => _toleranceMmText;
            set
            {
                if (_toleranceMmText == value) return;
                _toleranceMmText = value;
                OnPropertyChanged(nameof(ToleranceMmText));

                if (!TryParseNumber(value, out double parsedValue))
                {
                    InputValidationMessage = "허용 오차는 숫자로 입력해야 합니다.";
                    return;
                }

                if (parsedValue < 0 || double.IsNaN(parsedValue) || double.IsInfinity(parsedValue))
                {
                    InputValidationMessage = "허용 오차는 0 이상이어야 합니다.";
                    return;
                }

                if (Math.Abs(_toleranceMm - parsedValue) >= 0.0001)
                {
                    _toleranceMm = parsedValue;
                    OnPropertyChanged(nameof(ToleranceMm));
                }

                ClearInputValidationMessage();
            }
        }

        // LoadIntrinsicCalibrationCommand가 실행되면
        // LoadIntrinsicCalibration() 메서드를 실행한다
        public RelayCommand LoadIntrinsicCalibrationCommand { get; }
        public RelayCommand SaveReferenceCommand { get; }

        // Calibration 객체 자체가 존재하는지 확인
        public bool HasCalibrationData
        {
            get
            {
                lock (_calibrationDataLock)
                {
                    return _calibrationData != null;
                }
            }
        }

        // CameraMatrix, DistCoeffs가 준비되었는지 확인
        public bool IsIntrinsicLoaded =>
            GetCalibrationDataState(data => data.HasIntrinsic);

        // Homography가 준비되었는지 확인
        public bool IsHomographyReady =>
            GetCalibrationDataState(data => data.HasHomography);

        // Undistort + WarpPerspective 모두 가능한지 확인
        public bool IsCorrectionReady =>
            GetCalibrationDataState(data => data.IsReadyForCorrection);

        // 실제 mm 단위 검사를 진행해도 되는지 확인
        // Intrinsic + Homography가 모두 준비되어야 true
        public bool IsCalibrationReadyForInspection =>
            GetCalibrationDataState(data => data.IsReadyForCorrection);

        // 현재 corrected frame 기준 mmPerPixel 값
        // 보정이 준비되지 않은 상태에서는 0을 반환
        public double CurrentMmPerPixel =>
            GetCalibrationDataValue(data =>
                data.IsReadyForCorrection ? data.MmPerPixel : 0);

        public string ExpectedFrameSizeText
        {
            get
            {
                if (TryGetExpectedFrameSize(out int width, out int height))
                {
                    return $"{width} x {height}";
                }

                return "-";
            }
        }

        // Property 값이 변경될 때 호출되는 메서드, 변경된 Property 이름을 인자로 받아 PropertyChangedEventHandler로 전달
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void ApplyInitialSettings(InspectionSettings settings)
        {
            _expectedLongMm = settings.ExpectedLongMm;
            _expectedLongMmText = FormatNumber(settings.ExpectedLongMm);

            _expectedShortMm = settings.ExpectedShortMm;
            _expectedShortMmText = FormatNumber(settings.ExpectedShortMm);

            _toleranceMm = settings.ToleranceMm;
            _toleranceMmText = FormatNumber(settings.ToleranceMm);

            _undistortAlpha = settings.UndistortAlpha;
            _undistortAlphaText = FormatNumber(settings.UndistortAlpha);

            _intrinsicJsonPath = ResolvePathFromBaseDirectory(settings.IntrinsicCalibrationPath);
        }

        private void SaveReferenceFromLatestResult()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_settingsPath))
                {
                    InputValidationMessage = "Settings path is not available.";
                    return;
                }

                if (_latestInspectionResult == null ||
                    _latestInspectionResult.HoleMeasurements.Count == 0)
                {
                    InputValidationMessage = "저장할 구멍 측정 결과가 없습니다.";
                    return;
                }

                List<HoleMeasurement> detectedHoles = _latestInspectionResult.HoleMeasurements
                    .Where(hole => hole.IsDetected)
                    .OrderBy(hole => GetCornerOrder(hole.CornerName))
                    .ToList();

                if (detectedHoles.Count != 4)
                {
                    InputValidationMessage = $"기준 저장 실패: 검출된 구멍 수={detectedHoles.Count}, 필요=4.";
                    return;
                }

                _settings.ToleranceMm = ToleranceMm;
                _settings.ExpectedHoleDiameterMm = detectedHoles.Average(hole => hole.DiameterMm);
                _settings.HoleReferences = detectedHoles
                    .Select(hole => new HoleReference
                    {
                        CornerName = hole.CornerName,
                        CenterXmm = hole.CenterXmm,
                        CenterYmm = hole.CenterYmm,
                        DiameterMm = hole.DiameterMm,
                        Circularity = hole.Circularity
                    })
                    .ToList();

                _settingsService.Save(_settingsPath, _settings);

                InputValidationMessage =
                    $"기준값 저장 완료: 평균 지름={_settings.ExpectedHoleDiameterMm:F2}mm, 구멍={detectedHoles.Count}개.";
            }
            catch (Exception ex)
            {
                InputValidationMessage = $"기준값 저장 실패: {ex.Message}";
            }
        }

        private static int GetCornerOrder(string cornerName)
        {
            return cornerName switch
            {
                "LT" => 0,
                "RT" => 1,
                "RB" => 2,
                "LB" => 3,
                _ => 99
            };
        }

        private static string ResolvePathFromBaseDirectory(string path)
        {
            if (Path.IsPathRooted(path))
            {
                return path;
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
        }

        private bool GetCalibrationDataState(Func<CalibrationData, bool> selector)
        {
            lock (_calibrationDataLock)
            {
                return _calibrationData != null && selector(_calibrationData);
            }
        }

        private double GetCalibrationDataValue(Func<CalibrationData, double> selector)
        {
            lock (_calibrationDataLock)
            {
                return _calibrationData != null ? selector(_calibrationData) : 0;
            }
        }

        private void NotifyCalibrationPropertiesChanged()
        {
            OnPropertyChanged(nameof(HasCalibrationData));
            OnPropertyChanged(nameof(IsIntrinsicLoaded));
            OnPropertyChanged(nameof(IsHomographyReady));
            OnPropertyChanged(nameof(IsCorrectionReady));
            OnPropertyChanged(nameof(IsCalibrationReadyForInspection));
            OnPropertyChanged(nameof(CurrentMmPerPixel));
            OnPropertyChanged(nameof(ExpectedFrameSizeText));
        }

        private static bool TryParseNumber(string value, out double result)
        {
            return double.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.CurrentCulture,
                    out result) ||
                double.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out result);
        }

        private static bool IsValidPositiveDimension(double value)
        {
            return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.###", CultureInfo.CurrentCulture);
        }

        private void ClearInputValidationMessage()
        {
            InputValidationMessage = string.Empty;
        }

        public bool TryGetExpectedFrameSize(out int width, out int height)
        {
            lock (_calibrationDataLock)
            {
                if (_calibrationData == null)
                {
                    width = 0;
                    height = 0;
                    return false;
                }

                return _calibrationData.TryGetIntrinsicImageSize(out width, out height);
            }
        }

        public bool ValidateFrameSizeAgainstIntrinsic(
            int actualWidth,
            int actualHeight,
            out string message)
        {
            if (actualWidth <= 0 || actualHeight <= 0)
            {
                message = $"Invalid camera frame size: {actualWidth}x{actualHeight}";
                return false;
            }

            if (!TryGetExpectedFrameSize(out int expectedWidth, out int expectedHeight))
            {
                message = $"Camera frame size: {actualWidth}x{actualHeight}. Intrinsic calibration is not loaded yet.";
                return true;
            }

            if (actualWidth != expectedWidth || actualHeight != expectedHeight)
            {
                message =
                    $"Camera frame size mismatch. Expected={expectedWidth}x{expectedHeight}, Actual={actualWidth}x{actualHeight}.";
                return false;
            }

            message = $"Camera frame size verified: {actualWidth}x{actualHeight}.";
            return true;
        }

        // Service로부터 전달받은 검사 결과 1회를 이용해 viewModel 상태를 갱신
        public void ProcessInspectionResult(InspectionResult result)
        {
            // 최신 검사 결과를 InspectionResult 타입으로 저장
            _latestInspectionResult = result;

            // 현재 검사 결과의 State를 가져옴
            _currentDecisionState = result.State;

            // 기존 상태와 현재 상태를 비교하여 상태 변경 여부 판단 (true라면 상태 변경, false면 상태 동일)
            bool stateChanged = (_previousDecisionState != _currentDecisionState);

            // 기존 검사 상태가 Warning이 아니고, 현재 검사 상태가 Warning인 경우 true, 그게 아니라면 false
            bool isFirstWarning =
                (_previousDecisionState != InspectionState.Warning) &&
                (_currentDecisionState == InspectionState.Warning);

            // 아래 메서드의 동작에 따라 StatusText, WarningMessage를 갱신
            UpdateDisplayValues(_latestInspectionResult);

            // 마지막으로 UI에 표시한 상태를 현재 상태로 갱신
            _lastDisplayedState = _currentDecisionState;

            // 다음 사이클 비교를 위해 직전 판정 상태를 현재 판정 상태로 갱신
            _previousDecisionState = _currentDecisionState;
        }

        public void ClearInspectionDisplayForPreviewMode()
        {
            _latestInspectionResult = null;
            StatusText = "표시 전용";
            WarningMessage = string.Empty;
            MeasuredValueText = "-";
        }

        private void UpdateDisplayValues(InspectionResult result)
        {
            LastUpdatedTimeText = result.Timestamp.ToString("HH:mm:ss");

            switch (result.State)
            {
                case InspectionState.Ok:
                    StatusText = "정상";
                    WarningMessage = string.Empty;
                    MeasuredValueText = CreateMeasurementText(result);
                    break;

                case InspectionState.Warning:
                    StatusText = "경고";
                    WarningMessage = "허용 범위 초과";
                    MeasuredValueText = CreateMeasurementText(result);
                    break;

                case InspectionState.NoObject:
                    StatusText = "물체 없음";
                    WarningMessage = string.IsNullOrWhiteSpace(result.DetailMessage)
                        ? "검사 대상이 감지되지 않음"
                        : result.DetailMessage;
                    MeasuredValueText = "-";
                    break;

                case InspectionState.Error:
                    StatusText = "오류";
                    WarningMessage = "검사 처리 중 오류 발생";
                    MeasuredValueText = "-";
                    break;

                case InspectionState.CalibrationRequired:
                    StatusText = "Calibration Required";
                    WarningMessage = "Intrinsic calibration과 Homography 보정이 필요합니다.";
                    MeasuredValueText = "-";
                    break;
            }
        }

        private static string CreateMeasurementText(InspectionResult result)
        {
            if (result.HoleMeasurements.Count > 0)
            {
                var lines = result.HoleMeasurements.Select(hole =>
                {
                    if (!hole.IsDetected)
                    {
                        return $"{hole.CornerName}: NG  {hole.Message}";
                    }

                    string state = hole.IsInTolerance ? "OK" : "NG";
                    return
                        $"{hole.CornerName}: {state}  " +
                        $"x={hole.CenterXmm:F1} y={hole.CenterYmm:F1} " +
                        $"d={hole.DiameterMm:F2}mm " +
                        $"err={hole.DiameterErrorMm:F2}mm " +
                        $"posErr={hole.PositionErrorMm:F2}mm " +
                        $"circ={hole.Circularity:F2} " +
                        $"{hole.Message}";
                });

                string holeText = string.Join("\n", lines);

                if (!string.IsNullOrWhiteSpace(result.DetailMessage))
                {
                    holeText += $"\n{result.DetailMessage}";
                }

                return holeText;
            }

            string measuredText =
                $"긴 면: {result.MeasuredLongMm:F2} mm ({result.MeasuredLongPx:F0}px)\n" +
                $"짧은 면: {result.MeasuredShortMm:F2} mm ({result.MeasuredShortPx:F0}px)";

            if (!string.IsNullOrWhiteSpace(result.DetailMessage))
            {
                measuredText += $"\n{result.DetailMessage}";
            }

            return measuredText;
        }

        // JSON file을 불러옴
        // LoadIntrinsicCalibrationCommand가 실행되면 이 메서드를 실행
        private void LoadIntrinsicCalibration()
        {
            try
            {
                if (!File.Exists(_intrinsicJsonPath))
                {
                    CalibrationStatusText = $"Intrinsic JSON file not found: {_intrinsicJsonPath}";
                    return;
                }

                CalibrationData newCalibrationData = _calibrationService.LoadIntrinsicFromJson(_intrinsicJsonPath);

                lock (_calibrationDataLock)
                {
                    _calibrationData?.Dispose();
                    _calibrationData = newCalibrationData;
                }

                CalibrationStatusText = "Intrinsic calibration loaded.";

                NotifyCalibrationPropertiesChanged();
            }
            catch (Exception ex)
            {
                CalibrationStatusText = $"Failed to load intrinsic calibration: {ex.Message}";
            }
        }

        // Homography 계산을 진행할 수 있도록 메서드 호출, Homography가 구해졌는지 판단
        public bool UpdateHomographyFromFrame(Mat sourceFrame)
        {
            try
            {
                if (sourceFrame == null || sourceFrame.Empty())
                {
                    CalibrationStatusText = "Homography update failed: source frame is empty.";
                    return false;
                }

                bool success;
                string message;

                lock (_calibrationDataLock)
                {
                    if (_calibrationData == null || !_calibrationData.HasIntrinsic)
                    {
                        CalibrationStatusText = "Load intrinsic calibration first.";
                        return false;
                    }

                    success = _calibrationService.TryUpdateHomography(
                        sourceFrame,
                        _calibrationData,
                        out message);
                }

                if (success)
                {
                    CalibrationStatusText = $"Homography updated. {message}";
                }
                else
                {
                    CalibrationStatusText = $"Homography update failed. {message}";
                }

                NotifyCalibrationPropertiesChanged();

                return success;
            }
            catch (Exception ex)
            {
                CalibrationStatusText = $"Homography update failed: {ex.Message}";
                return false;
            }
        }

        public async Task<bool> UpdateHomographyFromFrameAsync(Mat sourceFrame)
        {
            try
            {
                if (sourceFrame == null || sourceFrame.Empty())
                {
                    CalibrationStatusText = "Homography update failed: source frame is empty.";
                    return false;
                }

                CalibrationStatusText = "Updating homography...";

                var updateResult = await Task.Run(() =>
                {
                    lock (_calibrationDataLock)
                    {
                        if (_calibrationData == null || !_calibrationData.HasIntrinsic)
                        {
                            return new
                            {
                                Success = false,
                                Message = "Load intrinsic calibration first."
                            };
                        }

                        bool success = _calibrationService.TryUpdateHomography(
                            sourceFrame,
                            _calibrationData,
                            out string message);

                        return new
                        {
                            Success = success,
                            Message = message
                        };
                    }
                });

                if (updateResult.Success)
                {
                    CalibrationStatusText = $"Homography updated. {updateResult.Message}";
                }
                else
                {
                    CalibrationStatusText = $"Homography update failed. {updateResult.Message}";
                }

                NotifyCalibrationPropertiesChanged();

                return updateResult.Success;
            }
            catch (Exception ex)
            {
                CalibrationStatusText = $"Homography update failed: {ex.Message}";
                return false;
            }
        }

        // sourceFrame이 준비되었는지에 대한 여부 확인
        // null이라면 보정이 아닌 기존 frame 사용, 아니라면 이 frame 사용 후 나중에 Dispose
        public Mat? CorrectFrameIfReady(Mat sourceFrame)
        {
            try
            {
                if (sourceFrame == null || sourceFrame.Empty())
                {
                    return null;
                }

                lock (_calibrationDataLock)
                {
                    if (_calibrationData == null || !_calibrationData.IsReadyForCorrection)
                    {
                        return null;
                    }

                    return _frameCorrectionService.Correct(sourceFrame, _calibrationData);
                }
            }
            catch (Exception ex)
            {
                CalibrationStatusText = $"Frame correction failed: {ex.Message}";
                return null;
            }
        }

        // sourceFrame에 Intrinsic 보정만 적용한다.
        // 반환된 Mat은 호출한 쪽에서 반드시 Dispose 해야 한다.
        // 새 좌표 변환 기반 검사 루트에서 사용할 메서드다.
        // 얘는 렌즈의 왜곡 (raw frame -> undistorted full frame으로)을 진행, WarpPerspective 안 함
        public Mat? UndistortFrameIfReady(Mat sourceFrame)
        {
            try
            {
                if (sourceFrame == null || sourceFrame.Empty())
                {
                    return null;
                }

                lock (_calibrationDataLock)
                {
                    if (_calibrationData == null || !_calibrationData.HasIntrinsic)
                    {
                        return null;
                    }

                    Mat undistorted = _frameCorrectionService.UndistortOnlyWithOptimalNewCameraMatrix(
                        sourceFrame,
                        _calibrationData,
                        UndistortAlpha,
                        out Rect validPixelRoi);

                    UpdateLatestValidPixelRoi(validPixelRoi);

                    string statusMessage =
                        $"Undistort alpha={UndistortAlpha:F2}, valid ROI={validPixelRoi.Width}x{validPixelRoi.Height} at ({validPixelRoi.X},{validPixelRoi.Y}).";

                    if (_lastUndistortStatusMessage != statusMessage)
                    {
                        _lastUndistortStatusMessage = statusMessage;
                        CalibrationStatusText = statusMessage;
                    }

                    return undistorted;
                }
            }
            catch (Exception ex)
            {
                CalibrationStatusText = $"Frame undistortion failed: {ex.Message}";
                ClearLatestValidPixelRoi();
                return null;
            }
        }

        public bool TryGetLatestValidPixelRoi(out Rect validPixelRoi)
        {
            lock (_validPixelRoiLock)
            {
                validPixelRoi = _latestValidPixelRoi;
                return _hasLatestValidPixelRoi;
            }
        }

        private void UpdateLatestValidPixelRoi(Rect validPixelRoi)
        {
            lock (_validPixelRoiLock)
            {
                _latestValidPixelRoi = validPixelRoi;
                _hasLatestValidPixelRoi =
                    validPixelRoi.Width > 0 &&
                    validPixelRoi.Height > 0;
            }
        }

        private void ClearLatestValidPixelRoi()
        {
            lock (_validPixelRoiLock)
            {
                _latestValidPixelRoi = default;
                _hasLatestValidPixelRoi = false;
            }
        }

        // 측정용 Homography를 Clone해서 외부에 제공한다.
        // CalibrationData가 보유한 원본 Homography를 직접 넘기지 않기 위한 메서드다.
        // 반환된 Mat은 호출한 쪽에서 반드시 Dispose 해야 한다.
        public Mat? GetHomographyCloneForMeasurement()
        {
            try
            {
                if (!TryGetMeasurementCalibrationSnapshot(
                    out Mat? homography,
                    out _))
                {
                    return null;
                }

                return homography;
            }
            catch (Exception ex)
            {
                CalibrationStatusText = $"Failed to get homography for measurement: {ex.Message}";
                return null;
            }
        }

        public bool TryGetMeasurementCalibrationSnapshot(out Mat? homography, out double mmPerPixel)
        {
            try
            {
                lock (_calibrationDataLock)
                {
                    if (_calibrationData == null || !_calibrationData.IsReadyForCorrection)
                    {
                        homography = null;
                        mmPerPixel = 0;
                        return false;
                    }

                    return _calibrationData.TryCreateHomographySnapshot(
                        out homography,
                        out mmPerPixel);
                }
            }
            catch (Exception ex)
            {
                CalibrationStatusText = $"Failed to get measurement calibration: {ex.Message}";
                homography = null;
                mmPerPixel = 0;
                return false;
            }
        }
    }
}
