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
using System.ComponentModel.Composition;
using Settings = HorizonCreator.Properties.Settings;

namespace HorizonCreator
{
    
    [Export(typeof(IPluginManifest))]
    public class HorizonCreator : PluginBase
    {

        [ImportingConstructor]
        public HorizonCreator() 
        {
            if (Settings.Default.UpdateSettings) 
            {
                Settings.Default.Upgrade();
                Settings.Default.UpdateSettings = false;
                CoreUtil.SaveSettings(Settings.Default);
            }
        }
    }
}
