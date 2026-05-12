using Camera_Insepctor_Project.Models;
using System;
using System.IO;
using System.Text.Json;

namespace Camera_Insepctor_Project.Services
{
    internal class InspectionSettingsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public InspectionSettings LoadOrCreateDefault(string settingsPath)
        {
            if (string.IsNullOrWhiteSpace(settingsPath))
            {
                throw new ArgumentException("Settings path is empty.", nameof(settingsPath));
            }

            if (!File.Exists(settingsPath))
            {
                InspectionSettings defaultSettings = new();
                WriteSettings(settingsPath, defaultSettings);
                return defaultSettings;
            }

            try
            {
                string json = File.ReadAllText(settingsPath);
                InspectionSettings? loadedSettings = JsonSerializer.Deserialize<InspectionSettings>(
                    json,
                    JsonOptions);

                return Normalize(loadedSettings ?? new InspectionSettings());
            }
            catch
            {
                return new InspectionSettings();
            }
        }

        public void Save(string settingsPath, InspectionSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settingsPath))
            {
                throw new ArgumentException("Settings path is empty.", nameof(settingsPath));
            }

            WriteSettings(settingsPath, Normalize(settings));
        }

        private static InspectionSettings Normalize(InspectionSettings settings)
        {
            InspectionSettings defaults = new();

            if (settings.CameraIndex < 0)
            {
                settings.CameraIndex = defaults.CameraIndex;
            }

            if (settings.RequestedCameraWidth <= 0)
            {
                settings.RequestedCameraWidth = defaults.RequestedCameraWidth;
            }

            if (settings.RequestedCameraHeight <= 0)
            {
                settings.RequestedCameraHeight = defaults.RequestedCameraHeight;
            }

            if (!IsPositiveFinite(settings.ExpectedLongMm))
            {
                settings.ExpectedLongMm = defaults.ExpectedLongMm;
            }

            if (!IsPositiveFinite(settings.ExpectedShortMm))
            {
                settings.ExpectedShortMm = defaults.ExpectedShortMm;
            }

            if (settings.ExpectedLongMm < settings.ExpectedShortMm)
            {
                settings.ExpectedLongMm = defaults.ExpectedLongMm;
                settings.ExpectedShortMm = defaults.ExpectedShortMm;
            }

            if (settings.ToleranceMm < 0 ||
                double.IsNaN(settings.ToleranceMm) ||
                double.IsInfinity(settings.ToleranceMm))
            {
                settings.ToleranceMm = defaults.ToleranceMm;
            }

            if (settings.UndistortAlpha < 0 ||
                settings.UndistortAlpha > 1 ||
                double.IsNaN(settings.UndistortAlpha) ||
                double.IsInfinity(settings.UndistortAlpha))
            {
                settings.UndistortAlpha = defaults.UndistortAlpha;
            }

            if (string.IsNullOrWhiteSpace(settings.IntrinsicCalibrationPath))
            {
                settings.IntrinsicCalibrationPath = defaults.IntrinsicCalibrationPath;
            }

            if (!IsPositiveFinite(settings.ExpectedHoleDiameterMm))
            {
                settings.ExpectedHoleDiameterMm = defaults.ExpectedHoleDiameterMm;
            }

            if (settings.HolePositionToleranceMm < 0 ||
                double.IsNaN(settings.HolePositionToleranceMm) ||
                double.IsInfinity(settings.HolePositionToleranceMm))
            {
                settings.HolePositionToleranceMm = defaults.HolePositionToleranceMm;
            }

            settings.HoleReferences ??= new List<HoleReference>();

            return settings;
        }

        private static bool IsPositiveFinite(double value)
        {
            return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static void WriteSettings(string settingsPath, InspectionSettings settings)
        {
            string? directoryPath = Path.GetDirectoryName(settingsPath);

            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(settingsPath, json);
        }
    }
}
