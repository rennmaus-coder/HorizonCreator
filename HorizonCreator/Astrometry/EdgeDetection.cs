#region "copyright"

/*
    Copyright © 2022 Christian Palm (christian@palm-family.de)
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Image.Interfaces;
using System;
using System.Threading.Tasks;

namespace HorizonCreator.Astrometry
{
    internal class EdgeDetection
    {

        public async Task<EdgeDetectionResult> Run(IExposureData exposure)
        {
            throw new NotImplementedException(); // TODO
        }
    }

    internal class EdgeDetectionResult
    {
        public IExposureData ExposureData { get; set; }
        public bool HasEdges { get; set; } = false;
        public object Edges { get; set; }
    }
}
