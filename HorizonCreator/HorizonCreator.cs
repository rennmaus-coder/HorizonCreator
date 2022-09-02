#region "copyright"

/*
    Copyright © 2022 Christian Palm (christian@palm-family.de)
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;
using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using Settings = HorizonCreator.Properties.Settings;

namespace HorizonCreator
{
    
    [Export(typeof(IPluginManifest))]
    public class HorizonCreator : PluginBase, INotifyPropertyChanged
    {
        public RelayCommand Calculate { get; set; }

        [ImportingConstructor]
        public HorizonCreator(IProfileService profile) 
        {
            if (Settings.Default.UpdateSettings) 
            {
                Settings.Default.Upgrade();
                Settings.Default.UpdateSettings = false;
                CoreUtil.SaveSettings(Settings.Default);
            }

            Calculate = new RelayCommand(_ =>
            {
                double exposureTime = profile.ActiveProfile.PlateSolveSettings.ExposureTime;
                int downloadTime = 3;
                int SlewTime = 10;
                int roughPlateSolveTime = 10;
                double timePerPlate = downloadTime + SlewTime + roughPlateSolveTime + exposureTime;

                double vertical = Math.Round((double)(StartAltitude / VerticalResolution));
                double horizontal = Math.Round((double)(360 / HorizontalResolution));
                vertical *= timePerPlate;
                horizontal *= vertical;

                MaxTime = new TimeSpan(0, 0, (int)horizontal);
            });
        }

        private TimeSpan _maxTime;
        public TimeSpan MaxTime
        {
            get { return _maxTime; }
            set
            {
                _maxTime = value;
                RaisePropertyChanged();
            }
        }

        public int HorizontalResolution 
        {
            get => Settings.Default.HorizontalResolution;
            set 
            { 
                Settings.Default.HorizontalResolution = value;
                RaisePropertyChanged();
            }
        }

        public int VerticalResolution
        {
            get => Settings.Default.VerticalResolution;
            set
            {
                Settings.Default.VerticalResolution = value;
                RaisePropertyChanged();
            }
        }

        public int StartAltitude
        {
            get => Settings.Default.StartAltitude;
            set
            {
                Settings.Default.StartAltitude = value;
                RaisePropertyChanged();
            }
        }

        public bool IsSouth
        {
            get => Settings.Default.IsSouth;
            set
            {
                Settings.Default.IsSouth = value;
                RaisePropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
