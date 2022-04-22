﻿using System.Reflection;
using System.Runtime.InteropServices;

// [MANDATORY] The following GUID is used as a unique identifier of the plugin. Generate a fresh one for your plugin!
[assembly: Guid("92fa2ba5-9642-44d5-aaea-3e5b9daa67fe")]

// [MANDATORY] The assembly versioning
//Should be incremented for each new release build of a plugin
[assembly: AssemblyVersion("1.0.0.1")]
[assembly: AssemblyFileVersion("1.0.0.1")]

// [MANDATORY] The name of your plugin
[assembly: AssemblyTitle("Horizon Creator")]
// [MANDATORY] A short description of your plugin
[assembly: AssemblyDescription("Create a horizon from within NINA only using your scope and mount!")]


// Your name
[assembly: AssemblyCompany("Christian Palm")]
// The product name that this plugin is part of
[assembly: AssemblyProduct("Horizon Creator")]
[assembly: AssemblyCopyright("Copyright ©  2022")]

// The minimum Version of N.I.N.A. that this plugin is compatible with
[assembly: AssemblyMetadata("MinimumApplicationVersion", "2.0.0.2059")]

// The license your plugin code is using
[assembly: AssemblyMetadata("License", "MPL-2.0")]
// The url to the license
[assembly: AssemblyMetadata("LicenseURL", "https://www.mozilla.org/en-US/MPL/2.0/")]
// The repository where your pluggin is hosted
[assembly: AssemblyMetadata("Repository", "https://github.com/rennmaus-coder/HorizonCreator")]

//[Optional] Common tags that quickly describe your plugin
[assembly: AssemblyMetadata("Tags", "Horizon,Dock")]

//[Optional] An in-depth description of your plugin
[assembly: AssemblyMetadata("LongDescription", @"How to use:
1. Connect to your mount (needs to be roughly polar aligned)
2. Move the mount to the obstruction (Scope Control plugin recommended), center it and press 'Add Point'. Repeat for all points you want to create a horizon from.
3. Click 'Save' and choose the desired location


To see your obstruction, I recommend making a looping exposure / live view or using an eyepiece")]