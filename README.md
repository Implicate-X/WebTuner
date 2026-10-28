# 📡 Web/FM Tuner

A hybrid of classic consumer electronics and modern .NET software architecture.  
The Web/FM Tuner is a fully standalone radio system that combines classic FM technology with web radio streaming.

The hardware, firmware, protocols, synchronization, and UI were developed entirely in-house—without off-the-shelf modules, third-party firmware, or proprietary black-box stacks.
Version 3.2 marks the final hardware generation of the device and forms the foundation for the upcoming software implementation on TinyCLR.

## 📑 Table of Contents
- Overview
- Hardware Architecture
- Changes from Previous Versions
- New Standby Architecture
- Binary Distance Decoder (BDD)
- Firmware Architecture
- Modules & Drivers
- Development Environment
- Outlook for Version 4.0

## 🧭 Overview
The Web/FM Tuner combines:

- TEF6686 automotive FM tuner
- VS1053B audio codec (MP3, AAC, Ogg, WAV)
- LS7366R quadrature encoder-counter
- Ethernet adapter for web radio
- VFD display (OLED option to follow)
- SITCore FEZ Feather as the central control CPU
- Wire-wrap underside for service-friendly wiring
- Firmware developed entirely in-house in C# / TinyCLR

The device is a true hybrid system:
Retro electronics meet modern .NET runtime.

## 🔧 Hardware Architecture
The main board, version 3.2, integrates:
- Power supply & transformer
- Audio path with switching between web and FM
- Ethernet module with its own 3.3 V domain
- Battery backup for wake-up
- FEZ Feather as the central controller
- Level-shifter subsystems
- I/O expander
- Encoder subsystem
- VFD display driver
- Wire-wrap backplane

All modules are clearly separated, logically grouped, and electrically well-routed.
## 🔄 Changes from previous versions
✔ BSS138 level shifter removed
Replaced by:
- 2× SN74LVC8T245
- 1× SN74LVC1T45

Result:
More stable levels, clean edges, fewer components, lower power dissipation.

✔ TEF6686 I²C level conversion via PCA9306

Result: Reliable communication even with long cables and mixed voltage domains.

✔ Ethernet module with its own 3.3 V power supply
Separate regulator for the LAN chip.
Result: Less interference, cleaner reception, more stable connection.

## 🔋 New Standby Architecture (Version 3.2)

The original FM tuner used a printed-circuit transformer permanently connected to the mains for standby.
This led to:

- Continuous power dissipation
- Thermal stress
- RF stray fields in the tuner area.

The new architecture completely replaces the transformer:
- LiPo-buffered FEZ Feather as the always-on core
- Feather runs in low-power mode
- Wake-up is triggered by a reed relay that activates the switching regulator before the main transformer
- The device is completely de-energized when turned off

Result:
No standby power loss, no mains stray fields, clean RF domain.

## 🔢 Binary Distance Decoder (BDD)
A proprietary IR decoding method for time-coded bit frames

The Web/FM tuner contains a custom-developed module for decoding a proprietary IR format that does not conform to the standard NEC specification.
The method was developed based on a comprehensive analysis of a remote control’s pulse duration sequences using a logic analyzer and was subsequently named the Binary Distance Decoder (BDD).
The BDD runs entirely on TinyCLR in C# 11 and is optimized for embedded real-time operation.
Operating Principle

The BDD format is a time-coded IR bitframe format consisting of:
- a pulse duration sequence,
- a sync gap as a start marker,
- 15 data bits in fixed time slots,
- a clear semantic structure:
- Bit Range Meaning
- 7 bits Command
- 5 bits Address
- 1 bit Toggle
- Remaining bits Reserve / Normalization

Complementary variants are merged into a canonical frame value via a normalization function.
Implementation is carried out using TinyCLR’s Signal Control API:
https://www.ghielectronics.com/docs/tinyclr/feature/signal-control

In this project, BDD is used to map IR remote control commands (e.g., Tuning +/–) to specific functions via a DENON code map.

## 💻 Firmware Architecture
The firmware is developed entirely in C# on TinyCLR:
- no C fragments
- no third-party stacks
- no proprietary libraries
- deterministic architecture
- clearly defined subsystems
- modular drivers
- full control over every layer

## 🎛 Modules & Drivers
Already stable in isolated test setups:
- TEF6686 – FM tuner with automotive DSP
- VS1053B – Audio codec for MP3, AAC, Ogg, WAV
- LS7366R – 32-bit quadrature encoder-counter
These modules are now being orchestrated and integrated into the overall system.

## 🧰 Development Environment
All components—firmware, drivers, gateway, server, Android app—are developed in a single development environment:
- Visual Studio 2026
- TinyCLR
- C#
- One runtime
- One codebase
Advantages:
- Consistent architecture
- Identical debugging tools
- No fragmentation between embedded, desktop, and mobile
- End-to-end maintainability

## 🔭 Outlook: Version 4.0
### 🖥️ OLED display (optional)
An OLED module with the exact form factor of the VFD is already prepared.
### 📱 Android app
Full control and monitoring of the device via an app—also written in C#.
### 🧩 Extended firmware
Synchronization, streaming logic, UI composition, logging, network stack.

## ❤️ TinyCLR OS

Designed for modern embedded development using .NET and TinyCLR OS.