# LocalTalk

Transfer your files between your devices!

Supports:

* ~~Windows Phone 8.x~~ (not fully implemented, won't even compile)
* UWP 10 (on-going)

## Capabilities

* Device discovery :white_check_mark:?
* HTTP(s) usage - not only Multicast :x:
* File pickers :x:
* File transfers :x:
* Localizations :x: - Universal method for both WP and UWP is needed

## Building

Requires Visual Studio 2017 (newers are not tested) with UWP +.NET workloads.

Build using MSBuild or the IDE itself as usual.

## Windows Phone 8

The code for this platform is in [LocalTalk](LocalTalk/).

Although it's very cool with supplied stuff from Microsoft, e.g Expandable Pane, it has some problems:

* No `.resw` support?
* No `using:` in XAML?
* No `x:String` and such support in XAML - creating a System namespace breaks building for UWP variant
* No `ListView`? OK I created an "alias" for it. And... it breaks building for UWP variant???

That's why I can bring this project to work there now - I **may** even abandon it.

It may build using 2017's MSBuild, but that will lose debugging using only that one green'y play button.