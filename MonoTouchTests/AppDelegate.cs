using System;
using System.Collections.Generic;
using System.Linq;
using MonoTouch.Foundation;
using MonoTouch.UIKit;
using MonoTouch.NUnit.UI;
using System.Reflection;

namespace MonoTouchTests
{
	// The UIApplicationDelegate for the application. This class is responsible for launching the 
	// User Interface of the application, as well as listening (and optionally responding) to 
	// application events from iOS.
	[Register ("AppDelegate")]
	public partial class AppDelegate : UIApplicationDelegate
	{
		// class-level declarations
		UIWindow window;
		TouchRunner runner;
		
		public override bool FinishedLaunching(UIApplication app, NSDictionary options)
		{
			window = new UIWindow(UIScreen.MainScreen.Bounds);
			runner = new TouchRunner(window);

			// tests can be inside the main assembly
			runner.Add(Assembly.GetExecutingAssembly());
#if false
			// you can use the default or set your own custom writer (e.g. save to web site and tweet it ;-)
			runner.Writer = new TcpTextWriter ("10.0.1.2", 16384);
			// start running the test suites as soon as the application is loaded
			runner.AutoStart = true;
			// crash the application (to ensure it's ended) and return to springboard
			runner.TerminateAfterExecution = true;
#endif
			window.RootViewController = new UINavigationController(runner.GetViewController());
			window.MakeKeyAndVisible();
			return true;
		}
	}
}

