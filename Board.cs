using System;
using System.Diagnostics;
using System.Threading;
using System.Collections.Generic;
using GHIElectronics.TinyCLR.Pins;
using GHIElectronics.TinyCLR.Devices.Gpio;
using GHIElectronics.TinyCLR.Devices.I2c;
using ImplicateX.TinyCLR.Drivers.Display;
using ImplicateX.TinyCLR.Drivers.Tuner;
using ImplicateX.TinyCLR.Drivers.Expander;
using ImplicateX.TinyCLR.Drivers.Decoder;
using ImplicateX.TinyCLR.Drivers.Decoder.IR;

namespace WebTuner
{
	public class Board
	{
		public static class I2cAddress
		{
			public const byte Expander = 0x20;
			public const byte RTC = 0x52;
			public const byte Tuner = 0x64;
		}

		public static class ControllerPin
		{
			public const int RemoteCtrl = FEZFeather.GpioPin.PA0;
			public const int EthernetCS = FEZFeather.GpioPin.PA3;
			public const int EthernetMISO = FEZFeather.GpioPin.PA6;
			public const int EthernetMOSI = FEZFeather.GpioPin.PA7;
			public const int EthernetSCLK = FEZFeather.GpioPin.PA5;
			public const int PowerSwitch = FEZFeather.GpioPin.PB1;
			public const int CodecSCLK = FEZFeather.GpioPin.PE12;
			public const int CodecMOSI = FEZFeather.GpioPin.PE14;
			public const int CodecMISO = FEZFeather.GpioPin.PE13;
			public const int EthernetINT = FEZFeather.GpioPin.PA10;
			public const int EthernetReset = FEZFeather.GpioPin.PA9;
			public const int DecoderSCLK = FEZFeather.GpioPin.PC8;
			public const int DecoderMOSI = FEZFeather.GpioPin.PC9;
			public const int DecoderMISO = FEZFeather.GpioPin.PC10;
			public const int DecoderCS = FEZFeather.GpioPin.PC11;
			public const int CodecGPIO1 = FEZFeather.GpioPin.PD2;

			public const int TunerRDS = FEZFeather.GpioPin.PE6;
			public const int CodecCS = FEZFeather.GpioPin.PE0;
			public const int CodecDCS = FEZFeather.GpioPin.PE1;
			public const int VFReset = FEZFeather.GpioPin.PD6;
			public const int VFDCS = FEZFeather.GpioPin.PD5;
			public const int VFDData = FEZFeather.GpioPin.PD4;
			public const int VFDClock = FEZFeather.GpioPin.PD3;
			public const int A1SCL = FEZFeather.GpioPin.PB8;
			public const int A1SDA = FEZFeather.GpioPin.PB9;
			public const int V1INTA = FEZFeather.GpioPin.PD0;
			public const int V1Reset = FEZFeather.GpioPin.PD1;
			public const int CodecDREQ = FEZFeather.GpioPin.PE7;
			public const int CodecReset = FEZFeather.GpioPin.PE8;
		}

		public static class ExpanderPin
		{
			public const int KeyMenu = 0;
			public const int KeyAutopreset = 1;
			public const int KeyBand = 2;
			public const int KeyDimmer = 3;
			public const int EncoderA = 5;
			public const int EncoderB = 6;
			public const int EncoderKey = 7;
			public const int V2OutputEnable = 8;
			public const int V3OutputEnable = 9;
			public const int AudioSwitch = 10;
			public const int TunerReset = 11;
			public const int RTCEVI = 12;
			public const int LedRDS = 13;
			public const int LedTuned = 14;
			public const int LedStereo = 15;
		}

		private GpioController gpioController = null!;
		private I2cController i2cController = null!;
		private Mcp23017 mcp23017 = null!;
		private Ls7366 quadDecoder = null!;
		private Vs1053 audioCodec = null!;
		private readonly SerialBus serialBus = null!;
		private readonly Network network = null!;
		private Generic16x2VFD vfd = null!;
		private BinaryDistanceDecoder bdd = null!;
		private Tef668x tef668x = null!;
		private GpioPin tunerResPin = null!;
		private GpioPin tunerRdsPin = null!;
		private GpioPin ledStereoPin = null!;
		private GpioPin ledTunedPin = null!;
		private GpioPin ledRdsPin = null!;
		private readonly List<byte> i2cAdresses = [ I2cAddress.Expander, I2cAddress.RTC, I2cAddress.Tuner ];

		// 8780, 8880, 9120, 9510 (WDR 3)*
		// 9650, 9810, 9920 (WDR2)*, 10130 (WDR 4)*
		// 10330 (WDRcosmo)*, 10600 (bigFM), 10670 (1LIVE)*
		private ushort tuneFrequency = 9920;
		private Tef668x.RdsSubSystem rdsSubSystem = null!;
		private bool infoThreadRunning = false;
		private Thread infoThread;
		private readonly bool EnableDebugOutput = true;
		private DateTime lastSignalDebugOutputUtc = DateTime.MinValue;
		private const int SignalUsableScoreThreshold = 20;
		private const int SignalLevel1Threshold = 80;
		private const int SignalLevel2Threshold = 130;
		private const int SignalLevel3Threshold = 180;
		private const int SignalLevel4Threshold = 240;
		private const int SignalLevel5Threshold = 320;

		public Board()
		{
			Initialize();
		}


		private void Initialize()
		{
			gpioController = GpioController.GetDefault();
			i2cController = I2cController.FromName( FEZDuino.I2cBus.I2c1 );

			#region I2C bus scan
			if( !i2cAdresses.TrueForAll( ProbeAddress ) )
			{
				Debug.WriteLine( "Warning: Not all expected I2C devices were found on the bus!" );
				return;
			}
			#endregion

			#region IO Expander MCP23017 Reset sequence
			GpioPin v1ResetPin = gpioController.OpenPin( ControllerPin.V1Reset );
			v1ResetPin.SetDriveMode( GpioPinDriveMode.Output );
			v1ResetPin.Write( GpioPinValue.High );
			Thread.Sleep( 100 );
			v1ResetPin.Write( GpioPinValue.Low );
			Thread.Sleep( 100 );
			v1ResetPin.Write( GpioPinValue.High );
			Thread.Sleep( 100 );
			#endregion

			#region IO Expander MCP23017 initialization
			var mcpDevice = CreateI2cDevice(
				i2cController,
				new I2cConnectionSettings( I2cAddress.Expander, I2cMode.Master, I2cAddressFormat.SevenBit ) );

			/// Interrupt pin for MCP23017 Port A is connected to V1INTA (PD0) on the controller
			var intPin = gpioController.OpenPin( ControllerPin.V1INTA );
			intPin.SetDriveMode( GpioPinDriveMode.InputPullUp );

			mcp23017 = new Mcp23017( mcpDevice, interruptPin: intPin );
			#endregion

			#region SN74LVC8T245 level shifter output enable pins
			GpioPin v2OutputEnable = mcp23017.OpenPin( ExpanderPin.V2OutputEnable );
			v2OutputEnable.SetDriveMode( GpioPinDriveMode.Output );
			v2OutputEnable.Write( GpioPinValue.Low );

			GpioPin v3OutputEnable = mcp23017.OpenPin( ExpanderPin.V3OutputEnable );
			v3OutputEnable.SetDriveMode( GpioPinDriveMode.Output );
			v3OutputEnable.Write( GpioPinValue.Low );
			#endregion

			#region Audio Switch ADG888 set to audio output from FM tuner TEF6686 module
			GpioPin audioSwitchPin = mcp23017.OpenPin( ExpanderPin.AudioSwitch );
			audioSwitchPin.SetDriveMode( GpioPinDriveMode.Output );
			audioSwitchPin.Write( GpioPinValue.High );
			#endregion
			
			#region Front panel keys pins
			GpioPin keyMenuPin = mcp23017.OpenPin( ExpanderPin.KeyMenu );
			keyMenuPin.SetDriveMode( GpioPinDriveMode.InputPullUp );
			GpioPin keyBandPin = mcp23017.OpenPin( ExpanderPin.KeyBand );
			keyBandPin.SetDriveMode( GpioPinDriveMode.InputPullUp );
			GpioPin keyAutopresetPin = mcp23017.OpenPin( ExpanderPin.KeyAutopreset );
			keyAutopresetPin.SetDriveMode( GpioPinDriveMode.InputPullUp );
			GpioPin keyDimmerPin = mcp23017.OpenPin( ExpanderPin.KeyDimmer );
			keyDimmerPin.SetDriveMode( GpioPinDriveMode.InputPullUp );
			#endregion

			#region Front panel LEDs pins
			ledTunedPin = mcp23017.OpenPin( ExpanderPin.LedTuned );
			ledTunedPin.SetDriveMode( GpioPinDriveMode.Output );
			ledTunedPin.Write( GpioPinValue.High );
			ledStereoPin = mcp23017.OpenPin( ExpanderPin.LedStereo );
			ledStereoPin.SetDriveMode( GpioPinDriveMode.Output );
			ledStereoPin.Write( GpioPinValue.High );
			ledRdsPin = mcp23017.OpenPin( ExpanderPin.LedRDS );
			ledRdsPin.SetDriveMode( GpioPinDriveMode.Output );
			ledRdsPin.Write( GpioPinValue.High );
			#endregion

			#region Front panel keys event handlers
			keyMenuPin.ValueChanged += ( sender, args ) =>
			{
				if( args.Edge == GpioPinEdge.FallingEdge )
				{
					ledTunedPin.Write( GpioPinValue.Low );
					Debug.WriteLine( $"Key Menu pin value changed to: {args.Edge}" );
				}
			};

			keyBandPin.ValueChanged += ( sender, args ) =>
			{
				if( args.Edge == GpioPinEdge.FallingEdge )
				{
					ledTunedPin.Write( GpioPinValue.High );
					Debug.WriteLine( $"Key Band pin value changed to: {args.Edge}" );
				}
			};

			keyAutopresetPin.ValueChanged += ( sender, args ) =>
			{
				if( args.Edge == GpioPinEdge.FallingEdge )
				{
					ledTunedPin.Write( GpioPinValue.High );
					Debug.WriteLine( $"Key Autopreset pin value changed to: {args.Edge}" );
				}
			};

			keyDimmerPin.ValueChanged += ( sender, args ) =>
			{
				if( args.Edge == GpioPinEdge.FallingEdge )
				{
					Debug.WriteLine( $"Key Dimmer pin value changed to: {args.Edge}" );
				}
			};
			#endregion

			#region LS7366R Quadrature Decoder/Counter
			quadDecoder = new(
				gpioController,
				ControllerPin.DecoderCS,
				ControllerPin.DecoderSCLK,
				ControllerPin.DecoderMOSI,
				ControllerPin.DecoderMISO );

			quadDecoder.CounterChanged += decoderEvent =>
			{
				ushort frequency = ( decoderEvent.Direction ) switch
				{
					( Ls7366.Direction.Up   ) => (ushort)ApplyTune( true, "Tune Up command received." ),
					( Ls7366.Direction.Down ) => (ushort)ApplyTune( false, "Tune Down command received." ),
					_ => 0
				};
			};

			quadDecoder.ResetCounter();
			quadDecoder.Start();
			#endregion

			#region Initialize Remote Control IR decoder
			GpioPin remoteCtrlPin = gpioController.OpenPin( ControllerPin.RemoteCtrl );
			remoteCtrlPin.SetDriveMode( GpioPinDriveMode.Input );
			#endregion

			#region Remote Control IR Binary Distance Decoder
			bdd = new BinaryDistanceDecoder( remoteCtrlPin );

			bdd.FrameDecoded += ( arg ) =>
			{
				BinaryDistanceDecoder.Code.DENON.TryGetValue( arg.RawValue, out string commandName );

				if( commandName == "Tuning +" )
				{
					ApplyTune( true, "Tune Up command received." );
				}
				else if( commandName == "Tuning -" )
				{
					ApplyTune( false, "Tune Down command received." );
				}
			};
			#endregion

			#region Initialize 16x2 Vacuum Fluorescent Display (VFD)
			vfd = new(
				gpioController,
				vfdClockPinId: ControllerPin.VFDClock,
				vfdDataPinId: ControllerPin.VFDData,
				vfdCSPinId: ControllerPin.VFDCS,
				vfResetPinId: ControllerPin.VFReset );

			vfd.SetScrollDelay( 200 );

			Thread vfdThread = new( () => vfd.Run() );

			vfdThread.Start();
			#endregion

			#region Initialize TEF6686 FM Tuner
			tunerResPin = mcp23017.OpenPin( ExpanderPin.TunerReset );
			tunerRdsPin = gpioController.OpenPin( ControllerPin.TunerRDS );

			tunerResPin.SetDriveMode( GpioPinDriveMode.Output );
			tunerRdsPin.SetDriveMode( GpioPinDriveMode.InputPullUp );

			tef668x = new(
				i2cControllerName: FEZDuino.I2cBus.I2c1,
				resPin: tunerResPin,
				rdsPin: tunerRdsPin );

			tef668x.Initialize();
			tef668x.SetAudioMute( mute: false );
			tef668x.TuneToFm( frequency10kHz: tuneFrequency );

			rdsSubSystem = tef668x.CreateRdsSubSystem( dataAvailableActiveHigh: false );
			rdsSubSystem.Start();

			infoThreadRunning = true;
			infoThread = new Thread( InfoThread );
			infoThread.Start();

			#endregion

			//_ = new Storage();

			//audioSwitchPin.Write( GpioPinValue.Low );

			//audioCodec = new Vs1053(
			//	uartControllerName: FEZDuino.UartPort.Uart1,
			//	spiControllerName: FEZDuino.SpiBus.Spi6,
			//	cmdCsPinID: FEZDuino.GpioPin.PC5,
			//	datCsPinID: FEZDuino.GpioPin.PC6,
			//	dreqPinID: FEZDuino.GpioPin.PC4,
			//	resetPinID: FEZDuino.GpioPin.PC7 );

			//audioCodec.Initialize();
			//audioCodec.SetVolume( leftChannel: 100, rightChannel: 100 );
			//audioCodec.RunStartupSineTest();
			//audioCodec.PlaySong( @"A:\Usa-Hymn.mid" );
			//audioCodec.PlaySong( @"A:\Abba- Thank You for the Music.mp3" );
			//audioCodec.PlaySong( @"A:\All by Myself.flac" );
			//audioCodec.PlaySong( @"A:\sample-12s.wav" );
			//audioCodec.PlaySong( @"A:\ogg_15s.ogg" );
			//audioCodec.PlaySong( @"A:\aac_15s.aac" );
			//audioCodec.PlaySong( @"A:\m4a_15s.m4a" );

		}


		private void InfoThread()
		{
			const int toggleIntervalMilliseconds = 3000;
			const int loopDelayMilliseconds = 200;
			DateTime nextToggleUtc = DateTime.UtcNow.AddMilliseconds( toggleIntervalMilliseconds );
			DateTime lastRdsUpdateUtc = DateTime.MinValue;
			bool showFrequencyAndSignal = true;
			string rdsLine1 = string.Empty;
			string rdsLine2 = string.Empty;
			ushort lastShownFrequency = tuneFrequency;

			while( infoThreadRunning )
			{
				if( tuneFrequency != lastShownFrequency )
				{
					lastShownFrequency = tuneFrequency;
					rdsLine1 = string.Empty;
					rdsLine2 = string.Empty;
					lastRdsUpdateUtc = DateTime.MinValue;
					showFrequencyAndSignal = true;
					nextToggleUtc = DateTime.UtcNow.AddMilliseconds( toggleIntervalMilliseconds );
				}

				bool hasNewFrames = false;

				while( rdsSubSystem.TryDequeueRdsFrame( out Tef668x.RdsFrame _ ) )
				{
					hasNewFrames = true;
				}

				if( hasNewFrames )
				{
					Tef668x.RdsDisplayModel model = rdsSubSystem.GetDisplayModel();

					rdsLine1 = $"{model.PsName} {model.PtyText}".Trim();
					rdsLine2 = model.RadioText ?? string.Empty;
					lastRdsUpdateUtc = DateTime.UtcNow;
				}

				bool hasRdsInfo =
					( !IsNullOrWhiteSpace( rdsLine1 ) || !IsNullOrWhiteSpace( rdsLine2 ) )
					&& ( DateTime.UtcNow - lastRdsUpdateUtc ).TotalSeconds <= 12;

				if( hasRdsInfo )
				{
					if( DateTime.UtcNow >= nextToggleUtc )
					{
						showFrequencyAndSignal = !showFrequencyAndSignal;
						nextToggleUtc = DateTime.UtcNow.AddMilliseconds( toggleIntervalMilliseconds );
					}

					if( showFrequencyAndSignal )
						vfd.SetLines( BuildFrequencyAndSignalLine(), rdsLine2 );
					else
						vfd.SetLines( rdsLine1, rdsLine2 );
				}
				else
				{
					rdsLine1 = string.Empty;
					rdsLine2 = string.Empty;
					showFrequencyAndSignal = true;
					nextToggleUtc = DateTime.UtcNow.AddMilliseconds( toggleIntervalMilliseconds );
					vfd.SetLines( BuildFrequencyAndSignalLine(), string.Empty );
				}

				Thread.Sleep( loopDelayMilliseconds );
			}
		}

		private string BuildFrequencyAndSignalLine()
		{
			string frequencyText = $"{tuneFrequency / 100.0:F1} MHz";
			int signalLevel = GetSignalLevel();
			string signalMeter = BuildSignalMeter( signalLevel );
			string line = $"{frequencyText} {signalMeter}";

			return FitToVfdWidth( line, 16 );
		}

		private int GetSignalLevel()
		{
			if( !tef668x.TryGetFmQualityData( out ushort qualityStatus, out short rfLevelDbuV10, out ushort usn, out ushort wam, out short offsetKhz10, out ushort bandwidthKhz10, out ushort modulation ) )
			{
				WriteSignalDebug( qualityStatus: 0, signalStatus: 0, rfLevelDbuV10: 0, hasUsableSignal: false, level: 0, note: "quality data read failed" );
				return 0;
			}

			if( !tef668x.TryGetFmSignalStatus( out ushort signalStatus ) )
			{
				signalStatus = 0;
			}

			int qualityTimeStamp = qualityStatus & 0x03FF;
			int score = rfLevelDbuV10 - ( ( usn + wam ) / 2 );
			bool hasUsableSignal = qualityTimeStamp > 0 && score > SignalUsableScoreThreshold;

			if( !hasUsableSignal )
			{
				WriteSignalDebug( qualityStatus, signalStatus, rfLevelDbuV10, hasUsableSignal: false, level: 0, note: $"score={score} usn={usn} wam={wam} off={offsetKhz10} bw={bandwidthKhz10} mod={modulation}" );
				return 0;
			}

			int level;

			if( score < SignalLevel1Threshold )
			{
				level = 1;
			}
			else if( score < SignalLevel2Threshold )
			{
				level = 2;
			}
			else if( score < SignalLevel3Threshold )
			{
				level = 3;
			}
			else if( score < SignalLevel4Threshold )
			{
				level = 4;
			}
			else if( score < SignalLevel5Threshold )
			{
				level = 5;
			}
			else
			{
				level = 6;
			}

			WriteSignalDebug( qualityStatus, signalStatus, rfLevelDbuV10, hasUsableSignal: true, level: level, note: $"score={score} usn={usn} wam={wam} off={offsetKhz10} bw={bandwidthKhz10} mod={modulation}" );
			return level;
		}

		private void WriteSignalDebug( ushort qualityStatus, ushort signalStatus, short rfLevelDbuV10, bool hasUsableSignal, int level, string note = "" )
		{
			if( !EnableDebugOutput )
			{
				return;
			}

			DateTime now = DateTime.UtcNow;

			if( ( now - lastSignalDebugOutputUtc ).TotalMilliseconds < 1000 )
			{
				return;
			}

			lastSignalDebugOutputUtc = now;
			int qualityTimeStamp = qualityStatus & 0x03FF;
			Debug.WriteLine( $"SignalDbg f={tuneFrequency} qs=0x{qualityStatus:X4} ts={qualityTimeStamp} rf={rfLevelDbuV10} s=0x{signalStatus:X4} valid={( hasUsableSignal ? 1 : 0 )} lvl={level} {note}" );
		}

		private static string BuildSignalMeter( int level )
		{
			if( level < 0 )
			{
				level = 0;
			}
			else if( level > 6 )
			{
				level = 6;
			}

			char[] meter = new char[ 6 ];

			for( int i = 0; i < meter.Length; i++ )
			{
				meter[ i ] = i < level ? (char)( 136 + i ) : ' ';
			}

			return new string( meter );
		}

		private static bool IsNullOrWhiteSpace( string value )
		{
			if( value == null || value.Length == 0 )
			{
				return true;
			}

			for( int i = 0; i < value.Length; i++ )
			{
				if( value[ i ] > ' ' )
				{
					return false;
				}
			}

			return true;
		}

		private static string FitToVfdWidth( string value, int width )
		{
			if( value == null )
			{
				value = string.Empty;
			}

			if( value.Length >= width )
			{
				return value.Substring( 0, width );
			}

			char[] text = new char[ width ];

			for( int i = 0; i < width; i++ )
			{
				text[ i ] = i < value.Length ? value[ i ] : ' ';
			}

			return new string( text );
		}

		/// <summary>
		/// Checks whether an I2C device responds at the specified 7-bit address.
		/// </summary>
		/// <remarks>Returns <see langword="false"/> if the I2C controller is not initialized or if the probe
		/// operation throws an exception.</remarks>
		/// <param name="address">7-bit I2C slave address to probe.</param>
		/// <returns><see langword="true"/> when the address is acknowledged; otherwise, <see langword="false"/>.</returns>
		private bool ProbeAddress( byte address )
		{
			bool result = false;

			if( i2cController == null )
			{
				Debug.WriteLine( "I2C controller not initialized!" );
				return false;
			}

			I2cDevice dev =
				i2cController.GetDevice(
					new I2cConnectionSettings( address, I2cMode.Master, I2cAddressFormat.SevenBit, 100_000U ) );

			byte[] rBuf = new byte[ 1 ];

			try
			{
				if( dev.ReadPartial( rBuf ).Status != I2cTransferStatus.SlaveAddressNotAcknowledged )
				{
					result = true;
				}
			}
			catch( Exception ex )
			{
				Debug.WriteLine( $"I2C probe failed for address 0x{address:X2}: {ex.Message}" );
				result = false;
			}

			dev.Dispose();

			return result;
		}

		private double ApplyTune( bool up, string logMessage )
		{
			Debug.WriteLine( logMessage );

			tuneFrequency += ( ushort )( up ? 10 : -10 );

			tuneFrequency = ( tuneFrequency ) switch
			{
				( <  8700 ) =>  8700,
				( > 10800 ) => 10800,
				_ => tuneFrequency
			};

			tef668x.TuneToFm( tuneFrequency );

			return 0.0;//tuner_.GetFrequencyMHz();
		}



		private static I2cDevice CreateI2cDevice( I2cController controller, I2cConnectionSettings settings )
		{
			var type = controller.GetType();
			var method =
				type.GetMethod( "CreateI2cDevice", new[] { typeof( I2cConnectionSettings ) } ) ??
				type.GetMethod( "GetDevice", new[] { typeof( I2cConnectionSettings ) } );

			if( method == null )
			{
				throw new NotSupportedException( "Keine kompatible Methode zum Erzeugen eines I2C-Geräts gefunden." );
			}

			return ( I2cDevice )method.Invoke( controller, new object[] { settings } );
		}
	}
}