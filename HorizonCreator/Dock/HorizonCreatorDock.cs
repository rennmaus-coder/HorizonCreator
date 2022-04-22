#region "copyright"

/*
    Copyright © 2022 Christian Palm (christian@palm-family.de)
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using HorizonCreator.Astrometry;
using NINA.Astrometry;
using NINA.Astrometry.Interfaces;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Equipment.Model;
using NINA.Image.Interfaces;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HorizonCreator.Dock
{
    [Export(typeof(IDockableVM))]
    public class HorizonCreatorDock : DockableVM
    {
        private IProfileService pService;
        private ITelescopeMediator telescope;
        private ICameraMediator camera;
        private IApplicationStatusMediator statusMediator;
        private IImagingMediator imaging;
        private CancellationTokenSource Token;
        private ApplicationStatus _status;

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

        public ApplicationStatus Status
        {
            get { return _status; }
            set
            {
                _status = value;
                _status.Source = "Horizon Creator";
                RaisePropertyChanged(nameof(Status));

                statusMediator.StatusUpdate(_status);
            }
        }

        public RelayCommand AddPoint { get; set; }
        public RelayCommand Save { get; set; }
        public RelayCommand Clear { get; set; }
        public RelayCommand Minus { get; set; }
        public RelayCommand AutomatedHorizon { get; set; }
        
        [ImportingConstructor]
        public HorizonCreatorDock(IProfileService profileService,
                                  ITelescopeMediator telescope,
                                  ICameraMediator camera,
                                  IApplicationStatusMediator statusMediator,
                                  IImagingMediator imaging) : base(profileService)
        {
            Title = "Horizon Creator";
            this.pService = profileService;
            this.telescope = telescope;
            this.camera = camera;
            this.statusMediator = statusMediator;
            this.imaging = imaging;
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
                else
                {
                    Notification.ShowWarning("Telescope is not connected!");
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
                    Notification.ShowSuccess("Horizon saved at " + dialog.FileName);
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

            AutomatedHorizon = new RelayCommand(async _ =>
            {
                
                if (telescope.GetInfo().Connected && telescope.GetInfo().CanMoveSecondaryAxis)
                {
                    Token = new CancellationTokenSource();
                    int progressDeg = 0;
                    int stepSize = 15;
                    double exposureTime = 2;
                    double startAlt = 80;
                    Angle latitude = Angle.ByDegree(pService.ActiveProfile.AstrometrySettings.Latitude);
                    Angle longitude = Angle.ByDegree(pService.ActiveProfile.AstrometrySettings.Longitude);
                    bool IsSouth = false;
                    if (latitude.Degree < 0)
                    {
                        IsSouth = true;
                    }
                    PierSide telescopeSide = telescope.GetInfo().SideOfPier;

                    await telescope.SlewToCoordinatesAsync(new Coordinates(IsSouth ? 90 : -90, 0, Epoch.JNOW, Coordinates.RAType.Degrees), Token.Token);

                    EdgeDetection detection = new EdgeDetection();
                    while (progressDeg < 360)
                    {
                        EdgeDetectionResult edges = new EdgeDetectionResult() { HasEdges = false };
                        do
                        {
                            TopocentricCoordinates topo = new TopocentricCoordinates(Angle.ByDegree(progressDeg + stepSize), Angle.ByDegree(startAlt), latitude, longitude);
                            if (!await telescope.SlewToCoordinatesAsync(topo, Token.Token))
                            {
                                Notification.ShowError("Slew failed! Check the logs for more information.");
                            }
                            IExposureData exposure = await imaging.CaptureImage(new CaptureSequence()
                            {
                                ExposureTime = exposureTime,
                                TotalExposureCount = 1,
                                ImageType = "SNAPSHOT"
                            }, Token.Token, new Progress<ApplicationStatus>(p => Status = p));

                            if (Token.IsCancellationRequested)
                            {
                                break;
                            }

                            edges = await detection.Run(exposure);
                        } while (!edges.HasEdges); 
                        // TODO: Add Coordinates to List
                    }
                }
                else
                {
                    Notification.ShowWarning("Telescope is not connected!");
                }
            });
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
