using System;
using System.Diagnostics;
using System.Threading;
using GHIElectronics.TinyCLR.Devices.Spi;
using GHIElectronics.TinyCLR.Devices.Gpio;
using GHIElectronics.TinyCLR.Pins;

namespace WebTuner
{
	internal class Program
	{
		private static void Main()
		{
			_ = new Board();

			while( true ) ;
		}
	}

}