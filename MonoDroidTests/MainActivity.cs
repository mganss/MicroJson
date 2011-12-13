using System;

using Android.App;
using Android.Content;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.OS;
using Android.NUnitLite.UI;
using NUnit.Framework;

namespace MonoDroidTests
{
    [Activity(Label = "MonoDroidTests", MainLauncher = true, Icon = "@drawable/icon")]
    public class MainActivity : RunnerActivity
    {
        protected override void OnCreate(Bundle bundle)
        {
            Add(typeof(MainActivity).Assembly);
            base.OnCreate(bundle);
        }
    }
}
