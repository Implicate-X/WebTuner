using System;
using System.Diagnostics;
using System.Threading;
using GHIElectronics.TinyCLR.Devices.Gpio;

namespace WebTuner
{
	public class VFD
	{
		private readonly GpioPin vfdClockPin;
		private readonly GpioPin vfdDataPin;
		private readonly GpioPin vfdCSPin;
		private readonly GpioPin vfResetPin;
		private const int LineLength = 16;
		private readonly string scrollPadding = new( ' ', LineLength );
		private string line1Text = string.Empty;
		private string line2Text = string.Empty;
		private int line1ScrollOffset;
		private int line2ScrollOffset;
		private long line1Revision;
		private long line2Revision;
		private int scrollDelayMs = 200;
		private readonly object syncRoot = new object();

		public VFD( GpioController gpioController )
		{
			vfdClockPin = gpioController.OpenPin( Board.ControllerPin.VFDClock );
			vfdDataPin = gpioController.OpenPin( Board.ControllerPin.VFDData );
			vfdCSPin = gpioController.OpenPin( Board.ControllerPin.VFDCS );
			vfResetPin = gpioController.OpenPin( Board.ControllerPin.VFReset );

			vfdClockPin.SetDriveMode( GpioPinDriveMode.Output );
			vfdDataPin.SetDriveMode( GpioPinDriveMode.Output );
			vfdCSPin.SetDriveMode( GpioPinDriveMode.Output );
			vfResetPin.SetDriveMode( GpioPinDriveMode.Output );

			vfdClockPin.Write( GpioPinValue.Low );
			vfdDataPin.Write( GpioPinValue.Low );
			vfdCSPin.Write( GpioPinValue.High );

			vfResetPin.Write( GpioPinValue.High );
			Thread.Sleep( 100 );
			vfResetPin.Write( GpioPinValue.Low );
			Thread.Sleep( 100 );
			vfResetPin.Write( GpioPinValue.High );
		}

		public void Run()
		{
			while( true )
			{
				string firstLine;
				string secondLine;
				int delayMs;
				int firstOffset;
				int secondOffset;
				long firstRevision;
				long secondRevision;

				lock( syncRoot )
				{
					firstLine = line1Text;
					secondLine = line2Text;
					delayMs = scrollDelayMs;
					firstOffset = line1ScrollOffset;
					secondOffset = line2ScrollOffset;
					firstRevision = line1Revision;
					secondRevision = line2Revision;
				}

				var frameCount = Math.Max( GetFrameCount( firstLine ), GetFrameCount( secondLine ) );

				SendFrame( firstLine, secondLine, firstOffset, secondOffset );
				Thread.Sleep( frameCount == 1 ? Math.Max( delayMs, 500 ) : delayMs );

				lock( syncRoot )
				{
					if( firstRevision == line1Revision && GetFrameCount( line1Text ) > 1 )
					{
						line1ScrollOffset = ( line1ScrollOffset + 1 ) % GetFrameCount( line1Text );
					}

					if( secondRevision == line2Revision && GetFrameCount( line2Text ) > 1 )
					{
						line2ScrollOffset = ( line2ScrollOffset + 1 ) % GetFrameCount( line2Text );
					}
				}
			}
		}

		public void SetLines( string firstLine, string secondLine )
		{
			lock( syncRoot )
			{
				firstLine = firstLine ?? string.Empty;
				secondLine = secondLine ?? string.Empty;

				if( !string.Equals( line1Text, firstLine ) )
				{
					line1Text = firstLine;
					line1ScrollOffset = 0;
					line1Revision++;
				}

				if( !string.Equals( line2Text, secondLine ) )
				{
					line2Text = secondLine;
					line2ScrollOffset = 0;
					line2Revision++;
				}
			}
		}

		public void SetLine1( string line )
		{
			lock( syncRoot )
			{
				line ??= string.Empty;

				if( !string.Equals( line1Text, line ) )
				{
					line1Text = line;
					line1ScrollOffset = 0;
					line1Revision++;
				}
			}
		}

		public void SetLine2( string line )
		{
			lock( syncRoot )
			{
				line ??= string.Empty;

				if( !string.Equals( line2Text, line ) )
				{
					line2Text = line;
					line2ScrollOffset = 0;
					line2Revision++;
				}
			}
		}

		public void SetScrollDelay( int milliseconds )
		{
			if( milliseconds < 20 )
			{
				milliseconds = 20;
			}

			lock( syncRoot )
			{
				scrollDelayMs = milliseconds;
			}
		}

		private int GetFrameCount( string text )
		{
			if( string.IsNullOrEmpty( text ) || text.Length <= LineLength )
			{
				return 1;
			}

			return text.Length + LineLength + 1;
		}

		private void SendFrame( string line1, string line2, int offset1, int offset2 )
		{
			SendWindow( [0x50, 0x80] );
			SendWindow( ComposeLineWindow( 0x10, line1, offset1 ) );
			SendWindow( ComposeLineWindow( 0x90, line2, offset2 ) );
			SendWindow( [0x70] );
		}

		private byte[] ComposeLineWindow( byte address, string text, int offset )
		{
			var bytes = new byte[ 1 + LineLength ];
			bytes[ 0 ] = address;

			var window = BuildWindow( text, offset );

			for( var i = 0; i < LineLength; i++ )
			{
				bytes[ i + 1 ] = ( byte )window[ i ];
			}

			return bytes;
		}

		private string BuildWindow( string text, int offset )
		{
			text ??= string.Empty;

			if( text.Length <= LineLength )
			{
				return Text.Reverse( Text.PadRight( text, LineLength ) );
			}

			var source = scrollPadding + text + scrollPadding;
			var result = new char[ LineLength ];

			for( var i = 0; i < LineLength; i++ )
			{
				var sourceIndex = offset + i;
				result[ i ] = sourceIndex < source.Length ? source[ sourceIndex ] : ' ';
			}

			return Text.Reverse( new string( result ) );
		}

		private void SendWindow( byte[] bytes )
		{
			vfdCSPin.Write( GpioPinValue.Low );

			for( var i = 0; i < bytes.Length; i++ )
			{
				SendByte( bytes[ i ] );
			}

			vfdCSPin.Write( GpioPinValue.High );
		}

		private void SendByte( byte value )
		{
			for( var bit = 0; bit < 8; bit++ )
			{
				vfdDataPin.Write( ( ( value >> bit ) & 0x01 ) != 0 ? GpioPinValue.High : GpioPinValue.Low );
				PulseClock();
			}
		}

		private void PulseClock()
		{
			vfdClockPin.Write( GpioPinValue.High );
			vfdClockPin.Write( GpioPinValue.Low );
		}
	}
}