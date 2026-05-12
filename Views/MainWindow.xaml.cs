using Camera_Insepctor_Project.Services;
using Camera_Insepctor_Project.ViewModels;
using Camera_Insepctor_Project.Models;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

/*
InitializeComponent()
DataContext = new MainViewModel()
버튼 이벤트 연결
창 종료 시 리소스 정리
 */

namespace Camera_Insepctor_Project
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {
        private readonly InspectionSettings _settings;
        private readonly MainViewModel _viewModel;
        private readonly InspectionService _inspectionService;

        private VideoCapture? _capture;
        private CancellationTokenSource? _cts;
        private Task? _cameraLoopTask;
        private bool _isClosing;
        private string _lastFrameSizeStatusMessage = string.Empty;

        // 카메라 루프와 버튼 클릭이 동시에 _latestFrame에 접근할 때 꼬이는 것을 막는 잠금객체
        private readonly object _latestFrameLock = new();
        // 가장 최근 frame을 저장함으로서 Undistort, Homography 연산 진행의 베이스
        private Mat? _latestFrame;

        public MainWindow()
        {
            InitializeComponent();

            _settings = LoadInitialSettings(out string settingsPath);

            // MainWindow 안에서 MainViewModel과 InspectionService를 직접 생성하여 사용
            _viewModel = new MainViewModel(_settings, settingsPath);
            _inspectionService = new InspectionService();

            // DataContext를 MainViewmodel로 설정하여, XAML에서 변수값을 불러올 시 _viewModel의 변수로부터 값을 가져오도록 함
            DataContext = _viewModel;
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing || _viewModel.IsRunning)
            {
                return;
            }

            try
            {
                _capture = new VideoCapture(_settings.CameraIndex);

                if (!_capture.IsOpened())
                {
                    _capture.Release();
                    _capture.Dispose();
                    _capture = null;

                    _viewModel.ProcessInspectionResult(new Models.InspectionResult
                    {
                        State = Models.InspectionState.Error,
                        MeasuredValue = 0,
                        IsInTolerance = false,
                        Timestamp = DateTime.Now
                    });
                    return;
                }

                ConfigureCameraResolution(_capture);

                _cts = new CancellationTokenSource();
                _viewModel.IsRunning = true;

                var token = _cts.Token;

                // 중요:
                // RunCameraLoopAsync를 직접 호출하면 UI thread에서 시작될 수 있음.
                // Task.Run으로 감싸서 camera read / OpenCV 처리 / frame correction을 background thread에서 실행한다.
                _cameraLoopTask = Task.Run(() => RunCameraLoopAsync(token));

                await _cameraLoopTask;
            }
            catch
            {
                _viewModel.IsRunning = false;
                if (!_isClosing)
                {
                    _viewModel.ProcessInspectionResult(new Models.InspectionResult
                    {
                        State = Models.InspectionState.Error,
                        MeasuredValue = 0,
                        IsInTolerance = false,
                        Timestamp = DateTime.Now
                    });
                }
            }
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            await StopCameraLoopAsync();
        }

        private async void UpdateHomographyButton_Click(object sender, RoutedEventArgs e)
        {
            Mat? frameForHomography = GetLatestFrameClone();

            if (frameForHomography == null)
            {
                _viewModel.CalibrationStatusText = "No camera frame available. Start camera first.";
                return;
            }

            try
            {
                using (frameForHomography)
                {
                    await _viewModel.UpdateHomographyFromFrameAsync(frameForHomography);
                }
            }
            catch (Exception ex)
            {
                _viewModel.CalibrationStatusText = $"Homography update failed: {ex.Message}";
            }
        }

        private async Task RunCameraLoopAsync(CancellationToken token)
        {
            try
            {
                using var frame = new Mat();

                while (!token.IsCancellationRequested)
                {
                    if (_capture == null || !_capture.IsOpened())
                    {
                        break;
                    }

                    bool success = _capture.Read(frame);

                    if (!success || frame.Empty())
                    {
                        await Task.Delay(30, token);
                        continue;
                    }

                    // 최신 frame을 매번 갱신
                    UpdateLatestFrame(frame);

                    if (!await ValidateFrameSizeForInspectionAsync(frame, token))
                    {
                        continue;
                    }

                    /* 실제 동작 확인용
                    using Mat displayFrame = frame.Clone();
                    using Mat inspectionFrame = frame.Clone();

                    var bitmapSource = displayFrame.ToBitmapSource();
                    bitmapSource.Freeze();

                    await Dispatcher.InvokeAsync(() =>
                    {
                        CameraImage.Source = bitmapSource;
                    });

                    var result = _inspectionService.Inspect(inspectionFrame);
                    */

                    // CorrectFrameIfReady를 호출, bool type에 따라 보정 frame을 쓸지 그냥 frame을 Clone할지
                    // 보정이 완료되지 않은 상태에서는 검사를 진행하지 않음
                    // 단, 카메라 화면은 계속 표시해서 사용자가 Homography를 업데이트할 수 있게 함
                    if (!_viewModel.IsCalibrationReadyForInspection)
                    {
                        using Mat? previewUndistortedFrame = CreateUndistortedPreviewFrameIfRequested(frame);
                        using Mat displayFrame = CreateDisplayFrame(frame, previewUndistortedFrame, null);

                        await Dispatcher.InvokeAsync(() =>
                        {
                            CameraImage.Source = CreateFrozenBitmapSource(displayFrame);

                            _viewModel.ProcessInspectionResult(new Models.InspectionResult
                            {
                                State = Models.InspectionState.CalibrationRequired,
                                MeasuredValue = 0,
                                IsInTolerance = false,
                                Timestamp = DateTime.Now
                            });
                        });

                        await Task.Delay(30, token);
                        continue;
                    }

                    // 여기까지 왔다는 것은 Intrinsic + Homography가 준비되었다는 뜻
                    // 새 방식에서는 이미지를 WarpPerspective 하지 않고,
                    // full frame에 Intrinsic 보정만 적용한 undistorted frame에서 검사한다.
                    Mat? undistortedFrame = _viewModel.UndistortFrameIfReady(frame);

                    if (undistortedFrame == null)
                    {
                        using Mat displayFrame = frame.Clone();

                        var undistortionFailedBitmap = displayFrame.ToBitmapSource();
                        undistortionFailedBitmap.Freeze();

                        await Dispatcher.InvokeAsync(() =>
                        {
                            CameraImage.Source = undistortionFailedBitmap;

                            _viewModel.ProcessInspectionResult(new Models.InspectionResult
                            {
                                State = Models.InspectionState.Error,
                                MeasuredValue = 0,
                                IsInTolerance = false,
                                Timestamp = DateTime.Now
                            });
                        });

                        await Task.Delay(30, token);
                        continue;
                    }

                    using (undistortedFrame)
                    {
                        if (!_viewModel.IsInspectionOverlaySelected)
                        {
                            using Mat displayFrame = CreateDisplayFrame(
                                frame,
                                undistortedFrame,
                                null);

                            await Dispatcher.InvokeAsync(() =>
                            {
                                CameraImage.Source = CreateFrozenBitmapSource(displayFrame);
                                _viewModel.ClearInspectionDisplayForPreviewMode();
                            });

                            await Task.Delay(30, token);
                            continue;
                        }

                        OpenCvSharp.Rect? validPixelRoi = _viewModel.TryGetLatestValidPixelRoi(
                            out OpenCvSharp.Rect latestValidPixelRoi)
                                ? latestValidPixelRoi
                                : null;

                        if (!_viewModel.TryGetMeasurementCalibrationSnapshot(
                            out Mat? homographyForMeasurement,
                            out double mmPerPixel) ||
                            homographyForMeasurement == null)
                        {
                            var homographyFailedBitmap = undistortedFrame.ToBitmapSource();
                            homographyFailedBitmap.Freeze();

                            await Dispatcher.InvokeAsync(() =>
                            {
                                CameraImage.Source = homographyFailedBitmap;

                                _viewModel.ProcessInspectionResult(new Models.InspectionResult
                                {
                                    State = Models.InspectionState.Error,
                                    MeasuredValue = 0,
                                    IsInTolerance = false,
                                    Timestamp = DateTime.Now
                                });
                            });

                            await Task.Delay(30, token);
                            continue;
                        }

                        using (homographyForMeasurement)
                        {
                            using Mat inspectionFrame = undistortedFrame.Clone();

                            var result = _inspectionService.InspectWithHomography(
                                inspectionFrame,
                                homographyForMeasurement,
                                mmPerPixel,
                                _viewModel.ExpectedLongMm,
                                _viewModel.ExpectedShortMm,
                                _viewModel.ToleranceMm,
                                _settings.ExpectedHoleDiameterMm,
                                _settings.HolePositionToleranceMm,
                                _settings.HoleReferences,
                                validPixelRoi);

                            using Mat displayFrame = CreateDisplayFrame(
                                frame,
                                undistortedFrame,
                                inspectionFrame);

                            await Dispatcher.InvokeAsync(() =>
                            {
                                CameraImage.Source = CreateFrozenBitmapSource(displayFrame);
                                _viewModel.ProcessInspectionResult(result);
                            });
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Stop 버튼에 의한 정상 취소
            }
            catch
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    _viewModel.ProcessInspectionResult(new Models.InspectionResult
                    {
                        State = Models.InspectionState.Error,
                        MeasuredValue = 0,
                        IsInTolerance = false,
                        Timestamp = DateTime.Now
                    });
                });
            }
            finally
            {
                _capture?.Release();
                _capture?.Dispose();
                _capture = null;

                _cts?.Dispose();
                _cts = null;

                ClearLatestFrame();

                if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        _viewModel.IsRunning = false;
                    });
                }
            }
        }

        private async Task StopCameraLoopAsync()
        {
            CancellationTokenSource? cts = _cts;
            Task? cameraLoopTask = _cameraLoopTask;

            if (cts == null && cameraLoopTask == null)
            {
                return;
            }

            cts?.Cancel();

            if (cameraLoopTask == null)
            {
                return;
            }

            try
            {
                await cameraLoopTask;
            }
            catch (OperationCanceledException)
            {
                // RunCameraLoopAsync에서 정상 취소로 처리하지만, 이중 안전장치로 둔다.
            }
        }

        private Mat? CreateUndistortedPreviewFrameIfRequested(Mat rawFrame)
        {
            if (_viewModel.SelectedDisplayMode == Models.CameraDisplayMode.Raw)
            {
                return null;
            }

            if (!_viewModel.IsIntrinsicLoaded)
            {
                return null;
            }

            return _viewModel.UndistortFrameIfReady(rawFrame);
        }

        private Mat CreateDisplayFrame(
            Mat rawFrame,
            Mat? undistortedFrame,
            Mat? inspectionOverlayFrame)
        {
            return _viewModel.SelectedDisplayMode switch
            {
                Models.CameraDisplayMode.Raw => rawFrame.Clone(),
                Models.CameraDisplayMode.Undistorted => undistortedFrame?.Clone() ?? rawFrame.Clone(),
                Models.CameraDisplayMode.InspectionOverlay => inspectionOverlayFrame?.Clone() ??
                    undistortedFrame?.Clone() ??
                    rawFrame.Clone(),
                _ => rawFrame.Clone()
            };
        }

        private static BitmapSource CreateFrozenBitmapSource(Mat frame)
        {
            var bitmapSource = frame.ToBitmapSource();
            bitmapSource.Freeze();
            return bitmapSource;
        }

        private void ConfigureCameraResolution(VideoCapture capture)
        {
            int targetWidth = _settings.RequestedCameraWidth;
            int targetHeight = _settings.RequestedCameraHeight;

            if (_viewModel.TryGetExpectedFrameSize(out int expectedWidth, out int expectedHeight))
            {
                targetWidth = expectedWidth;
                targetHeight = expectedHeight;
            }

            capture.Set(VideoCaptureProperties.FrameWidth, targetWidth);
            capture.Set(VideoCaptureProperties.FrameHeight, targetHeight);

            double reportedWidth = capture.Get(VideoCaptureProperties.FrameWidth);
            double reportedHeight = capture.Get(VideoCaptureProperties.FrameHeight);

            _viewModel.CalibrationStatusText =
                $"Camera resolution requested: {targetWidth}x{targetHeight}, driver reported: {reportedWidth:F0}x{reportedHeight:F0}.";
        }

        private static InspectionSettings LoadInitialSettings(out string settingsPath)
        {
            settingsPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Configuration",
                "inspection_settings.json");

            var settingsService = new InspectionSettingsService();
            return settingsService.LoadOrCreateDefault(settingsPath);
        }

        private async Task<bool> ValidateFrameSizeForInspectionAsync(Mat frame, CancellationToken token)
        {
            if (!_viewModel.TryGetExpectedFrameSize(out _, out _))
            {
                return true;
            }

            int actualWidth = frame.Width;
            int actualHeight = frame.Height;

            bool isFrameSizeValid = _viewModel.ValidateFrameSizeAgainstIntrinsic(
                actualWidth,
                actualHeight,
                out string message);

            if (_lastFrameSizeStatusMessage != message)
            {
                _lastFrameSizeStatusMessage = message;

                await Dispatcher.InvokeAsync(() =>
                {
                    _viewModel.CalibrationStatusText = message;
                });
            }

            if (isFrameSizeValid)
            {
                return true;
            }

            using Mat displayFrame = frame.Clone();

            var bitmapSource = displayFrame.ToBitmapSource();
            bitmapSource.Freeze();

            await Dispatcher.InvokeAsync(() =>
            {
                CameraImage.Source = bitmapSource;

                _viewModel.ProcessInspectionResult(new Models.InspectionResult
                {
                    State = Models.InspectionState.Error,
                    MeasuredValue = 0,
                    IsInTolerance = false,
                    Timestamp = DateTime.Now
                });
            });

            await Task.Delay(30, token);
            return false;
        }

        // 새 frame이 들어오면 복사본 생성
        // 기존 _latestFrame을 Dispose하고, 새 frame을 _latestFrame에 넣음
        private void UpdateLatestFrame(Mat sourceFrame)
        {
            Mat clonedFrame = sourceFrame.Clone();

            lock (_latestFrameLock)
            {
                _latestFrame?.Dispose();
                _latestFrame = clonedFrame;
            }
        }

        // 얘는 저장된 가장 최신 frame을 Clone하여 반환
        private Mat? GetLatestFrameClone()
        {
            lock (_latestFrameLock)
            {
                if (_latestFrame == null || _latestFrame.Empty())
                {
                    return null;
                }

                return _latestFrame.Clone();
            }
        }

        // 검사가 멈추거나 창이 종료될 때 저장되어 있던 _latestFrame을 정리
        private void ClearLatestFrame()
        {
            lock (_latestFrameLock)
            {
                _latestFrame?.Dispose();
                _latestFrame = null;
            }
        }

        protected override async void OnClosed(EventArgs e)
        {
            _isClosing = true;

            await StopCameraLoopAsync();

            base.OnClosed(e);
        }
    }
}
