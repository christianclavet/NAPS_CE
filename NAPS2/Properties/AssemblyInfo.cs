using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

// General Information about an assembly is controlled through the following 
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("NAPS2 - Cricri Edition")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("NAPS2 - Cricri Edition")]
[assembly: AssemblyCopyright("Copyright 2009, 2012-2026 NAPS2 Contributors;")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]


// Setting ComVisible to false makes the types in this assembly not visible 
// to COM components.  If you need to access a type in this assembly from 
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The application is not DPI-aware (see app.manifest): Windows scales it on high-DPI screens.
// The thumbnail decoder uses WPF (WIC), and WPF makes the whole process DPI-aware the first time it is used,
// which made the window lose its scaling (everything became small on a 4K screen) when a project was loaded.
// WPF reads this attribute on the entry assembly and then leaves the DPI awareness unchanged.
[assembly: System.Windows.Media.DisableDpiAwareness]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("1c3a7177-f3a7-439e-be47-e304a185f932")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version 
//      Build Number
//      Revision
//
[assembly: AssemblyVersion("1.1.0.*")]
