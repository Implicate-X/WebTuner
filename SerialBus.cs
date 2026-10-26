using System.Diagnostics;
using System.IO;
using System.Threading;
using GHIElectronics.TinyCLR.Devices.Storage;
using GHIElectronics.TinyCLR.Devices.UsbHost;
using GHIElectronics.TinyCLR.Devices.UsbHost.Descriptors;
using GHIElectronics.TinyCLR.Pins;
using GHIElectronics.TinyCLR.IO;
using System.Text;
using System;

namespace WebTuner
{
	public class SerialBus
	{
		private readonly UsbHostController usbHostController = UsbHostController.GetDefault();

		public SerialBus()
		{
			usbHostController.OnConnectionChangedEvent += UsbHostController_OnConnectionChangedEvent;
			usbHostController.Enable();

		}

		private void UsbHostController_OnConnectionChangedEvent( UsbHostController sender, DeviceConnectionEventArgs e )
		{
			Debug.WriteLine( "Id=" + e.Id + " interface=" + e.InterfaceIndex + " port=" + e.PortNumber
						  + " type=" + e.Type + " vid=" + e.VendorId + " pid=" + e.ProductId );

			switch( e.DeviceStatus )
			{
				case DeviceConnectionStatus.Connected:
					HandleConnect( e );
					break;
				case DeviceConnectionStatus.Disconnected:
					Debug.WriteLine( "Device disconnected" );
					// Unmount file system here if previously mounted.
					break;
				case DeviceConnectionStatus.Bad:
					Debug.WriteLine( "Bad device" );
					break;
			}
		}

		void HandleConnect( DeviceConnectionEventArgs e )
		{
			switch( e.Type )
			{
				case BaseDevice.DeviceType.Keyboard:
					var keyboard = new Keyboard( e.Id, e.InterfaceIndex );
					keyboard.KeyDown += ( s, a ) => Debug.WriteLine( "Key down: " + a.Which + " (ASCII " + a.ASCII + ")" );
					keyboard.KeyUp += ( s, a ) => Debug.WriteLine( "Key up: " + a.Which + " (ASCII " + a.ASCII + ")" );
					break;

				case BaseDevice.DeviceType.Mouse:
					var mouse = new Mouse( e.Id, e.InterfaceIndex );
					mouse.CursorMoved += ( s, a ) => Debug.WriteLine( "Mouse: " + a.NewPosition.X + "," + a.NewPosition.Y );
					mouse.ButtonChanged += ( s, a ) => Debug.WriteLine( "Mouse button: " + a.Which );
					break;

				case BaseDevice.DeviceType.Joystick:
					var joystick = new Joystick( e.Id, e.InterfaceIndex );
					joystick.CursorMoved += ( s, a ) => Debug.WriteLine( "Joystick: " + a.NewPosition.X + "," + a.NewPosition.Y );
					joystick.HatSwitchPressed += ( s, a ) => Debug.WriteLine( "Joystick hat: " + a.Direction );
					joystick.ButtonChanged += ( s, a ) => Debug.WriteLine( "Joystick button: " + a.Which );
					break;

				case BaseDevice.DeviceType.MassStorage:
					MountMassStorage();

					FileEnumerator files = new( @"A:\\", FileEnumFlags.Files );

					foreach( var file in files )
					{
						Debug.WriteLine( "Found file: " + file );

					}

					//using( var fs = new FileStream( @"A:\autorun.inf", FileMode.Open, FileAccess.Read ) )
					//using( TextReader reader = new StreamReader( fs ) )
					//{
					//	string line;
					//	while( ( line = reader.ReadLine() ) != null )
					//	{
					//		Debug.WriteLine( line );
					//	}
					//}
					break;

				default:
					ReadRawDevice( e );
					break;
			}
		}
		void MountMassStorage()
		{
			var storage = StorageController.FromName( SC20100.StorageController.UsbHostMassStorage );
			var drive = FileSystem.Mount( storage.Hdc );
			var info = new DriveInfo( drive.Name );

			Debug.WriteLine( "Volume: " + info.VolumeLabel );
			Debug.WriteLine( "Format: " + info.DriveFormat );
			Debug.WriteLine( "Total size: " + info.TotalSize );
			Debug.WriteLine( "Free space: " + info.TotalFreeSpace );
			Debug.WriteLine( "Root: " + info.RootDirectory );
		}

		void ReadRawDevice( DeviceConnectionEventArgs e )
		{
			var raw = new RawDevice( e.Id, e.InterfaceIndex, e.Type );

			var deviceDescriptor = raw.GetDeviceDescriptor();
			var configDescriptor = raw.GetConfigurationDescriptor( 0 );

			// SET_CONFIGURATION(1) — standard setup packet to activate config 1.
			raw.SendSetupPacket( 0, 9, 1, 0 );

			// Build an Endpoint descriptor for an interrupt-in endpoint (address 0x81, 8-byte max packet, 10 ms poll).
			var endpointData = new byte[ 7 ]
			{
				7,        // Descriptor length.
				5,        // Endpoint descriptor type.
				0x81,     // Input endpoint, address 1.
				3,        // Interrupt transfer type.
				8, 0,     // Max packet size = 8 (LSB, MSB).
				10,       // Polling interval (ms).
			};

			var endpoint = new Endpoint( endpointData, 0 );
			var pipe = raw.OpenPipe( endpoint );
			pipe.TransferTimeout = 10;

			var data = new byte[ 8 ];
			var read = pipe.Transfer( data );

			if( read > 0 )
			{
				Debug.WriteLine( "Raw device read " + read + " bytes: "
							  + data[ 0 ] + "," + data[ 1 ] + "," + data[ 2 ] + "," + data[ 3 ] );
			}
			else
			{
				Debug.WriteLine( "No data available" );
			}

			Thread.Sleep( 500 );
		}
	}
}
