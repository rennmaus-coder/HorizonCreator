#region "copyright"

/*
    Copyright © 2022 Christian Palm (christian@palm-family.de)
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using OxyPlot;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading.Tasks;

namespace HorizonCreator.Dock
{

    [Export(typeof(IDockableVM))]
    public class HorizonCreatorDock : DockableVM, ITelescopeConsumer
    {
        private IProfileService pService;
        private ITelescopeMediator telescope;

        private List<Coordinate> points;
        private string x;

        public string Points
        {
            get
            {
                string res = string.Empty;
                foreach (var item in points)
                {
                    res += $"{Math.Round(item.Azimuth, 2).ToString("000.00")}      {Math.Round(item.Altitude, 2).ToString("00.00")}\n";
                }
                return res;
            }
            set
            {
                x = value;
                RaisePropertyChanged();
            }
        }

        private double altitudeIncrement;

        public double AltitudeIncrement
        {
            get => altitudeIncrement;
            set
            {
                altitudeIncrement = value;
                RaisePropertyChanged();
            }
        }

        private double azimuthIncrement;

        public double AzimuthIncrement
        {
            get => azimuthIncrement;
            set
            {
                azimuthIncrement = value;
                RaisePropertyChanged();
            }
        }

        public RelayCommand AddPoint { get; set; }
        public RelayCommand Save { get; set; }
        public RelayCommand Clear { get; set; }
        public RelayCommand Minus { get; set; }

        public IAsyncCommand MoveAltAzCommand { get; }

        [ImportingConstructor]
        public HorizonCreatorDock(IProfileService profileService, ITelescopeMediator telescope) : base(profileService)
        {
            Title = "Horizon Creator";
            this.pService = profileService;
            this.telescope = telescope;
            telescope.RegisterConsumer(this);

            Horizon = new List<HorizonPoint>();
            AltitudeIncrement = 5;
            AzimuthIncrement = 5;

            points = new List<Coordinate>();

            AddPoint = new RelayCommand(_ => {
                if (telescope.GetInfo().Connected)
                {
                    double altitude = telescope.GetInfo().Altitude;
                    double azimuth = telescope.GetInfo().Azimuth;

                    List<Coordinate> temp = points;
                    temp.Add(new Coordinate() { Altitude = altitude, Azimuth = azimuth });
                    points = temp;
                    RaisePropertyChanged(nameof(Points));
                    RefreshHorizon();
                }
            });

            Save = new RelayCommand(_ => {
                Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save Horizon",
                    FileName = "horizon",
                    DefaultExt = ".hrz",
                    Filter = "Horizon File|*.hrz"
                };

                if (dialog.ShowDialog() == true)
                {
                    File.WriteAllText(dialog.FileName, Points);
                }
            });

            Clear = new RelayCommand(_ => {
                points = new List<Coordinate>();
                RaisePropertyChanged(nameof(Points));
                RefreshHorizon();
            });

            Minus = new RelayCommand(_ => {
                if (points.Count > 0)
                {
                    List<Coordinate> temp = points;
                    temp.RemoveAt(temp.Count - 1);
                    points = temp;
                    RaisePropertyChanged(nameof(Points));
                    RefreshHorizon();
                }
            });

            MoveAltAzCommand = new AsyncCommand<bool>(MoveAltAz, (object o) => telescope.GetInfo().Connected);
        }

        private void RefreshHorizon()
        {
            var h = new List<HorizonPoint>();
            if (points.Count == 0)
            {
            }
            else if (points.Count == 1)
            {
                h.Add(new HorizonPoint(points[0].Altitude, points[0].Azimuth));
            }
            else
            {
                using (var textReader = new StringReader(Points))
                {
                    var customHorizon = CustomHorizon.FromReader_Standard(textReader);
                    for (int azimuth = 0; azimuth < 360; azimuth++)
                    {
                        var horizonAltitude = customHorizon.GetAltitude(azimuth);
                        h.Add(new HorizonPoint(horizonAltitude, azimuth));
                    }
                }
            }
            var exH = new List<HorizonPoint>();
            foreach (var p in points)
            {
                exH.Add(new HorizonPoint(p.Altitude, p.Azimuth));
            }
            Horizon = h;
            ExplicitHorizonPoints = exH;
        }

        private List<HorizonPoint> horizon;

        public List<HorizonPoint> Horizon
        {
            get
            {
                return horizon;
            }
            private set
            {
                horizon = value;
                RaisePropertyChanged();
            }
        }

        private List<HorizonPoint> explicitHorizonPoints;

        public List<HorizonPoint> ExplicitHorizonPoints
        {
            get
            {
                return explicitHorizonPoints;
            }
            private set
            {
                explicitHorizonPoints = value;
                RaisePropertyChanged();
            }
        }

        private DataPoint telescopePosition;

        public DataPoint TelescopePosition
        {
            get => telescopePosition;
            set
            {
                if (telescopePosition.X != value.X || telescopePosition.Y != value.Y)
                {
                    telescopePosition = value;
                    RaisePropertyChanged();
                }
            }
        }

        private async Task<bool> MoveAltAz(object arg)
        {
            if (arg is string s)
            {
                var latitude = Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude);
                var longitude = Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude);
                var info = telescope.GetInfo();
                var altitude = Angle.ByDegree(info.Altitude);
                var azimuth = Angle.ByDegree(info.Azimuth);
                switch (s)
                {
                    case "N":
                        altitude += Angle.ByDegree(AltitudeIncrement);
                        break;

                    case "S":
                        altitude -= Angle.ByDegree(AltitudeIncrement);
                        break;

                    case "E":
                        azimuth += Angle.ByDegree(AzimuthIncrement);
                        break;

                    case "W":
                        azimuth -= Angle.ByDegree(AzimuthIncrement);
                        break;
                }

                var topo = new TopocentricCoordinates(azimuth, altitude, latitude, longitude);
                await telescope.SlewToCoordinatesAsync(topo, default);
            }
            return true;
        }

        public void UpdateDeviceInfo(TelescopeInfo deviceInfo)
        {
            if (IsVisible)
            {
                try
                {
                    if (deviceInfo.Connected)
                    {
                        TelescopePosition = new DataPoint(-deviceInfo.Altitude, deviceInfo.Azimuth);
                    }
                    else
                    {
                        TelescopePosition = new DataPoint(0, 0);
                    }
                }
                catch { }
            }
        }

        public void Dispose()
        {
            this.telescope.RemoveConsumer(this);
        }
    }

    public class Coordinate
    {
        public double Altitude { get; set; }
        public double Azimuth { get; set; }

        public override string ToString()
        {
            return $"{Azimuth} {Altitude}";
        }
    }

    public class HorizonPoint
    {

        public HorizonPoint(double altitude, double azimuth)
        {
            this.X = -altitude;
            this.Y = azimuth;
        }

        public double X { get; }
        public double Y { get; }
        public double Y2 => 0;
        public double X2 => 0;
    }
}