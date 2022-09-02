#region "copyright"

/*
    Copyright © 2022 Christian Palm (christian@palm-family.de)
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Astrometry;
using NINA.Astrometry.Interfaces;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading.Tasks;

namespace HorizonCreator.Dock
{
    [Export(typeof(IDockableVM))]
    public class HorizonCreatorDock : DockableVM
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
                    res += $"{item.Azimuth}      {item.Altitude}\n";
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
        public double AltitudeIncrement {
            get => altitudeIncrement;
            set {
                altitudeIncrement = value;
                RaisePropertyChanged();
            }
        }

        private double azimuthIncrement;
        public double AzimuthIncrement {
            get => azimuthIncrement;
            set {
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

            AltitudeIncrement = 5;
            AzimuthIncrement = 5;

            points = new List<Coordinate>();

            AddPoint = new RelayCommand(_ =>
            {
                if (telescope.GetInfo().Connected)
                {
                    double altitude = telescope.GetInfo().Altitude;
                    double azimuth = telescope.GetInfo().Azimuth;

                    List<Coordinate> temp = points;
                    temp.Add(new Coordinate() { Altitude = altitude, Azimuth = azimuth });
                    points = temp;
                    RaisePropertyChanged(nameof(Points));
                }
            });

            Save = new RelayCommand(_ =>
            {
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

            Clear = new RelayCommand(_ =>
            {
                points = new List<Coordinate>();
                RaisePropertyChanged(nameof(Points));
            });

            Minus = new RelayCommand(_ =>
            {
                if (points.Count > 0)
                {
                    List<Coordinate> temp = points;
                    temp.RemoveAt(temp.Count - 1);
                    points = temp;
                    RaisePropertyChanged(nameof(Points));
                }
            });

            MoveAltAzCommand = new AsyncCommand<bool>(MoveAltAz, (object o) => telescope.GetInfo().Connected);
        }

        private async Task<bool> MoveAltAz(object arg) {
            if(arg is string s) {
                var latitude = Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude);
                var longitude = Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude);
                var info = telescope.GetInfo();
                var altitude = Angle.ByDegree(info.Altitude);
                var azimuth = Angle.ByDegree(info.Azimuth);
                switch(s) {
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
}
