# TinyFanControl Laptop

This is the laptop line of TinyFanControl. It is a separate repository so the desktop app at [TinyFanControl](https://github.com/stressced/TinyFanControl) can stay at poll rate zero. The two apps use different mutexes, executables, and settings folders: this one writes to `%AppData%\TinyFanControl-Laptop`.

This snapshot is still the desktop fan controller. Laptop fan backends are not wired in yet. `laptop-fans/OmenMon` is a submodule of [OmenMon](https://github.com/OmenMon/OmenMon) (GPL-3), checked out as reference and excluded from the build. After cloning, run `git submodule update --init`. The prebuilt `OmenMon.exe` and `OmenMon.sys` under `laptop-fans/OmenMon (Fancoolers Victus)/` stay on this machine and are not committed.

Until a laptop backend lands, the behavior below is unchanged: TinyFanControl Laptop sets motherboard fan speeds and then stays completely idle. Its poll rate is zero. It never runs a background refresh. While you are in a game it does not read sensors, rewrite fan registers, or schedule any work, so it adds nothing to stutter or frametimes.

You choose a duty, press Apply, and that speed is written once. The process then sits in the tray and does no further hardware work until you change a fan or quit.

## Zero poll rate

A fan tool only stays out of a game's frame pacing if it stops talking to the hardware. TinyFanControl Laptop never starts a polling loop.

- No timer, no refresh interval, and no background thread.
- No periodic temperature, RPM, or duty reads.
- No periodic writes to fan-control registers.
- The open window does not keep sampling. The percentage on screen is the duty you set, not a live reading ticking in the background.

Hardware is touched only because you did something:

| When | What happens |
| --- | --- |
| Startup | Controls are discovered once. Each saved manual speed is written once. |
| Apply | That fan's duty is written once. |
| Reset, or a profile entry saved as automatic | That header is handed back to the BIOS and read once, so the slider matches the firmware. |
| Open the window | Fans still under BIOS control are read once, so an automatic slider is not showing a stale number. Manual fans are left alone. |
| Exit | Every header is released and the hardware session is closed. |

Nothing is scheduled between those moments. With the app in the tray during a match, the poll rate stays at 0. There is no refresh tick that can land inside a frame.

Settings, the four profiles, and the window position are written to disk only when you save, delete, or close. There is no file watcher.

## What you can do

- Set any detected motherboard or controller fan to a fixed duty from 0 to 100 percent. Dragging the slider only changes the number on screen. The register is written when you press Apply.
- Hand a header back to its BIOS curve with Reset.
- Keep four profile slots. Left-click an empty slot to capture the current speeds. Left-click a filled slot to apply them. Right-click a filled slot to clear it.
- Leave the app in the system tray. Double-click the icon, or choose Open Controls, to bring the window back.
- Support development on Patreon or follow Stressced on X using the footer links, which open in your default browser.
- Speeds and profiles are restored the next time the app starts. A manual fan is written once at launch and is not sampled again.

Minimizing hides the window to the tray and leaves your speeds in place. Closing the window quits the app and returns fan control to the BIOS.

## Requirements

- Windows 10 or 11, 64-bit
- Administrator rights. The app requests them, because LibreHardwareMonitor needs them to reach the Super I/O.
- The PawniO driver, which LibreHardwareMonitor uses for chipset register access

## PawniO

LibreHardwareMonitor reaches fan, temperature, and voltage registers through the signed PawniO kernel driver. Without it, TinyFanControl Laptop starts normally and reports that no fan controls were detected. Other monitoring tools can still work, because they ship their own drivers.

1. Download a current [LibreHardwareMonitor release](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases).
2. Run `LibreHardwareMonitor.exe` and accept the PawniO install prompt if it appears.
3. Start TinyFanControl Laptop again.

## Build

The project targets .NET 8 for Windows, with WPF.

The executable, window, and tray share `Assets/stressced.ico`. Its editable
source is `Assets/tinyfan.svg`: the angular T with the Silicon Tuner purple
palette, a transparent background, and no outline. After changing the vector,
run `./gen_icon.ps1` before building. It generates the application icon and the
root `stressced.ico` in sizes 16, 24, 32, 48, 64, 128, and 256 pixels, plus
`Assets/tinyfan.png` for the window header. Patreon and X icon paths are from
Simple Icons (CC0 1.0), matching Silicon Tuner.

Self-contained single file (portable, about 73 MB):

```powershell
dotnet publish -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o ./publish
```

`IncludeNativeLibrariesForSelfExtract` keeps the WPF native libraries inside the executable. Without it, `wpfgfx_cor3.dll`, `PresentationNative_cor3.dll`, and three other native DLLs are left beside the exe, so the build is not standalone. `EnableCompressionInSingleFile` brings the executable down from about 157 MB to about 73 MB.

Framework-dependent build (smaller; needs the .NET 8 desktop runtime):

```powershell
dotnet publish -c Release -r win-x64 -o ./publish
```

## Fan control notes

Use LibreHardwareMonitorLib 0.9.7 or newer. On 0.9.6 the NCT6687 driver scales writes to the System Fan channels by roughly ten times, so a requested 40 percent is programmed as about 4 percent. That sits under a typical fan start threshold, the fan stops, and it reports 0 RPM. CPU Fan and Pump Fan are not affected. This project references 0.9.7-pre726.

Leaving the app hands the fans back to the BIOS. `SetDefault()` releases each control, and `Computer.Close()` rewrites every control register with the value the library snapshotted when it opened. Manual speeds apply only while the app is running. They are saved and written again on the next launch.

A header driven by a real BIOS curve, such as a CPU fan, is usually reclaimed within a few seconds of `SetDefault()`, because the firmware keeps writing that register. A header configured as a fixed duty has no such loop: the firmware writes it once during POST and does not touch it again, so releasing it changes nothing visible and the register keeps the last value written to it.

On the board this was written against, System Fan 5 is a fixed 70 percent with no curve, which is why releasing it looks like nothing happened. That header returns to the BIOS value only when `Close()` rolls the register back, or after a reboot. If the process is killed instead of closed, the pinned duty remains, and the next launch snapshots that value and restores it on exit. Reboot to recover the BIOS duty.

The same interaction is what made the 0.9.6 scaling bug stick. A System Fan header was written near 4 percent instead of 40 percent, the BIOS never corrected a fixed-duty header, and every later launch snapshotted the near-zero value and restored it on exit.
