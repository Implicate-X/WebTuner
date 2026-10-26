/// <summary>
/// IR Binary Distance Decoder (BDD) decoder and supporting types under TinyCLR 3 with C# 11
/// for SITCore embedded devices, both from GHI Electronics, LLC - https://www.ghielectronics.com/docs/tinyclr/.
///
/// This file implements a small runtime component used to capture pulse timings from an IR receiver 
/// connected to a GPIO pin, decode frames encoded by a binary-distance-style protocol, 
/// normalize complementary frame pairs, and raise events for consumed frames.
///
/// The public surface provides:
/// - `FrameEventArgs` :
///    A container for decoded frame data (raw value, bit count and convenient accessors for command/address/toggle).
/// - `BinaryDistanceDecoder` : a background capture/decoder which exposes the `FrameDecoded` event 
///    and a `TryDecode` helper for decoding captured pulse durations.
///
/// Decoder notes:
/// - `TryDecode` inspects a sequence of pulse durations (in microseconds) 
///    and reconstructs a raw frame value bit-by-bit when timing thresholds are matched. 
///    The method returns `true` when a complete frame is decoded.
/// - `NormalizeRaw` maps complement-equivalent raw values to a canonical (lower) representation 
///    to reduce duplicate events emitted for the same logical command.
///
/// (c) 2026 Robert Dettmann  
/// 
/// </summary>
using System;
using System.Threading;
using GHIElectronics.TinyCLR.Devices.Gpio;
using GHIElectronics.TinyCLR.Devices.Signals;

namespace Device.IR
{
    public sealed class BinaryDistanceDecoder
    {
		/// <summary>
		/// Provides decoded Binary Distance Decoder (BDD) frame data.
		/// </summary>
		/// <param name="rawValue">The raw decoded frame value as produced by the decoder.</param>
		/// <param name="bitCount">The number of decoded bits contained in <paramref name="rawValue"/>.</param>
		public sealed class FrameEventArgs( ushort rawValue, int bitCount ) : EventArgs
		{
			/// <summary>
			/// Gets the raw decoded frame value.
			/// The raw value is assembled bit-by-bit by the decoder and may include
			/// command, address and control bits depending on the protocol variant.
			/// </summary>
			public ushort RawValue { get; } = rawValue;

			/// <summary>
			/// Gets the number of decoded bits.
			/// This indicates how many meaningful bits were shifted into <see cref="RawValue"/>.
			/// </summary>
			public int BitCount { get; } = bitCount;

			/// <summary>
			/// Gets the 7-bit command portion of the frame.
			/// Computed from bits 6:0 of <see cref="RawValue"/>.
			/// </summary>
			public byte Command => ( byte )( this.RawValue & 0x7F );

			/// <summary>
			/// Gets the 5-bit address portion of the frame.
			/// Computed from bits 11:7 of <see cref="RawValue"/>.
			/// </summary>
			public byte Address => ( byte )( ( this.RawValue >> 7 ) & 0x1F );

			/// <summary>
			/// Gets a value indicating whether the toggle/control bit is set.
			/// Computed from bit 12 of <see cref="RawValue"/>.
			/// </summary>
			public bool Toggle => ( ( this.RawValue >> 12 ) & 0x01 ) != 0;
		}

		/// <summary>
		/// Captures and decodes IR pulses encoded with a binary distance protocol.
		/// </summary>
		/// <remarks>
		/// A background worker continuously reads pulse timings from the configured GPIO pin,
		/// decodes frames, normalizes equivalent complement pairs, and publishes deduplicated results
		/// via the <see cref="FrameDecoded"/> event.
		///
		/// The class encapsulates:
		/// - GPIO pin setup for a typical IR receiver (input with pull-up).
		/// - Construction and configuration of a <see cref="SignalCapture"/> instance.
		/// - A background thread that reads captured pulse durations, attempts decoding,
		///   performs normalization and debounce/deduplication, then raises events.
		/// </remarks>

		private readonly SignalCapture capture_;
        private readonly TimeSpan[] buffer_ = new TimeSpan[ 256 ];
        private ushort lastPublishedRaw_;
        private long lastPublishedTicks_;

		/// <summary>
		/// Occurs when a valid BDD frame has been decoded.
		/// Subscribers receive a <see cref="FrameEventArgs"/> containing the raw
		/// frame value and bit count. The decoder attempts to suppress duplicate
		/// deliveries of the same logical frame within a short time window.
		/// </summary>
		public event Action<FrameEventArgs> FrameDecoded;

		/// <summary>
		/// Initializes a new instance of the <see cref="BinaryDistanceDecoder"/> class.
		/// The constructor configures the provided GPIO pin and starts the background
		/// capture/decoder thread.
		/// </summary>
		/// <param name="pin">The GPIO pin connected to the IR receiver output.</param>
		/// <exception cref="ArgumentNullException">
		/// Thrown when <paramref name="pin"/> is <see langword="null"/>.
		/// </exception>
		public BinaryDistanceDecoder( GpioPin pin )
        {
            if( pin == null )
            {
                throw new ArgumentNullException( nameof( pin ) );
            }

            pin.SetDriveMode( GpioPinDriveMode.InputPullUp );

            this.capture_ = new SignalCapture( pin )
            {
                Timeout = TimeSpan.FromMilliseconds( 100 ),
                DisableInterrupts = false
            };

            new Thread( this.Worker ).Start();
        }

        /// <summary>
        /// Background capture loop that reads pulse data and emits decoded frames.
        /// </summary>
        /// <remarks>
        /// The loop:
        /// - Reads captured signal durations via <see cref="SignalCapture.Read"/>.
        /// - Filters out too-short captures.
        /// - Attempts to decode a frame using <see cref="TryDecode"/>.
        /// - Normalizes complement-equivalent frames with <see cref="NormalizeRaw"/>.
        /// - Applies a short-time deduplication filter before invoking <see cref="FrameDecoded"/>.
        /// </remarks>
        private void Worker()
        {
            while( true )
            {
                try
                {
                    int count = this.capture_.Read( out GpioPinValue initial, this.buffer_ );

                    if( count < 10 )
                    {
                        continue;
                    }

                    if( TryDecode( initial, this.buffer_, count, out ushort rawValue, out int bitCount ) )
                    {
                        rawValue = NormalizeRaw( rawValue );
                        long nowTicks = DateTime.UtcNow.Ticks;

                        if( rawValue == this.lastPublishedRaw_ && ( nowTicks - this.lastPublishedTicks_ ) < TimeSpan.FromMilliseconds( 180 ).Ticks )
                        {
                            continue;
                        }

                        this.lastPublishedRaw_ = rawValue;
                        this.lastPublishedTicks_ = nowTicks;

						this.FrameDecoded?.Invoke( new FrameEventArgs( rawValue, bitCount ) );
					}
                }
                catch
                {
                    Thread.Sleep( 5 );
                }
            }
        }

        /// <summary>
        /// Normalizes a raw BDD value by selecting the lower value from a complement-equivalent pair.
        /// </summary>
        /// <param name="rawValue">The raw value to normalize.</param>
        /// <returns>The normalized raw value.</returns>
        /// <remarks>
        /// Some binary-distance-style encodings produce complementary raw values that represent
        /// the same logical command. This helper computes the bitwise complement with respect
        /// to a mask (0x03FF) and returns the numerically lower of the two values to canonicalize.
        /// </remarks>
        private static ushort NormalizeRaw( ushort rawValue )
        {
            ushort complementPair = (ushort)( rawValue ^ 0x03FFU );
            return complementPair < rawValue ? complementPair : rawValue;
        }

        /// <summary>
        /// Attempts to decode a BDD frame from captured pulse durations.
        /// </summary>
        /// <param name="initial">The initial pin level before the first pulse duration.</param>
        /// <param name="pulses">Pulse duration buffer from signal capture (TimeSpan ticks are used).</param>
        /// <param name="count">Number of valid pulse entries to process.</param>
        /// <param name="rawValue">When this method returns <see langword="true"/>, contains the decoded raw frame value.</param>
        /// <param name="bitCount">When this method returns <see langword="true"/>, contains the number of decoded bits.</param>
        /// <returns>
        /// <see langword="true"/> if a complete frame (as defined by the decoder's bit-count threshold) was decoded; otherwise, <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// The decoder:
        /// - Iterates captured pulse durations converting TimeSpan ticks to microseconds.
        /// - Detects a long sync gap (us > 8000) to begin a new frame.
        /// - Interprets subsequent high-level pulses with durations within a defined window
        ///   (300..2400 us) as data bits. Longer pulses within that window indicate bit '1'.
        /// - Assembles bits into <paramref name="rawValue"/> and increments <paramref name="bitCount"/>.
        /// - Stops and returns true once the expected number of bits has been received.
        /// </remarks>
        public static bool TryDecode( GpioPinValue initial, TimeSpan[] pulses, int count, out ushort rawValue, out int bitCount )
        {
            rawValue = 0;
            bitCount = 0;

            if( pulses == null || count <= 0 || count > pulses.Length )
            {
                return false;
            }

            bool inFrame = false;
            GpioPinValue level = initial;

            for( int i = 0; i < count; i++ )
            {
                long us = pulses[ i ].Ticks / 10L;

                if( us < 0 )
                {
                    us = -us;
                }

                if( us > 8000 )
                {
                    inFrame = true;
                    rawValue = 0;
                    bitCount = 0;

                    level = level == GpioPinValue.High ? GpioPinValue.Low : GpioPinValue.High;

                    continue;
                }

                if( inFrame && level == GpioPinValue.High )
                {
                    if( us >= 300 && us <= 2400 )
                    {
                        rawValue <<= 1;

                        if( us > 1200 )
                        {
                            rawValue |= 1;
                        }

                        bitCount++;

                        if( bitCount >= 15 )
                        {
                            return true;
                        }
                    }
                }

                level = level == GpioPinValue.High ? GpioPinValue.Low : GpioPinValue.High;
            }

            return false;
        }
    }
}