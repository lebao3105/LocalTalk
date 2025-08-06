# LocalTalk contributing documentation

> This is intended for developers only. For translators, read the final section.

Thank you for joining us! This documentation is made to clarify my choices about stuff, and what to do to avoid build+runtime errors and such.

And since there was a PR breaking a lot of things, writing this is more critical.

Many informations below are taken from a project called WhatsWin, although it's not UWP 10.

## Project structure

You can see that there are 2 projects, excluding the [`Shared`](Shared/) one:

* `LocalTalk`: What LocalTalk is meant to be - a Windows Phone 8.1 project;
* `LocalTalkUWP`: LocalTalk, but Universal - Universal in Universal Windows Platform. Not for Windows 8.1 though.
* The shared project has its own [README](Shared/README.md).

> Fact: Universal Windows was used in Windows 8.1 era - try creating a new project in Visual Studio 2015 and see what I mean.
> Not much people if not nobody cares about this, and Universal Windows Platform was known more as a thing in Windows 10 era.
> If you want to use this pharse, specify the version: 8.1 or 10.

Both two projects bring the shared code to the light - they are GUI projects, front-ends of the underlying LocalSend protocol implementation after all.

The UWP project targets Windows 2004 (build 19041), minimuns Windows Anniversary Update (build 14393). **Do NOT just change these!**

## Requirements

* Know C#. But note: the UWP version uses C# 7.3, while the Windows Phone one uses C# 6.0.
* Know about the limitations in these projects. Since I target old versions of Windows, and this project was mainly for WP 8, limitations will show up.
* Know about Git and GitHub.
* [**For UWP**] Have Visual Studio 2017 with UWP and .NET workloads. Also install needed SDKs.
* [**For WP**] Have Visual Studio 2015 with Windows 8.1 SDK and Tools.
* Working internet connection - for both installing VS, building (restore NuGet packages) and using LocalTalk itself.

## Limitations and considerations

### CSharp

Visual Studio 2017 is the first version of Visual Studio to drop Windows Phone support (NOT Windows 10 Mobile as it is a part of the UWP 10 family).

That's all that we know. However its MSBuild can build projects using Windows 8.x SDK - Windows 8.1 SDK even appears in VS 2017 installer.

2019 is unknown to be able to do that, and 2022 is confirmed to not build until we change the minimum target version.

C# 7, comes with Visual Studio 2017, brings useful things - see the changelog [here](https://devblogs.microsoft.com/dotnet/new-features-in-c-7-0/) and [here](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-version-history#c-version-71).

(don't think we will use all of them though)

C# 7.3 is used by default in the UWP version. C# [6](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-version-history#c-version-60) is used for the WP one.

See the problem?

The shared project is not able to specify C# version it's gonna use and such, it follows the project that uses it.

And a shared project gets used by both variants. That is the problem: if you use C# 7 there, the build will break for WP platform.

The fix is to...use Visual Studio 2017 for the WP project. You'll have to manually deploy and attach debuggers to the application if you do that.

### Technologies

Stuff like cryptography, XAML controls can be different. Currently there is no remarkable thing.

But surely that the WP version may get older stuff than the UWP one.

## Localization

They are located in [Shared/Resources/Strings](Shared/Resources/Strings).

Each folder present their corresponding language (code), contain 2 things:

* `Resources.resw` where most strings are placed;
* `Miscs.xaml` where the rest are.

Edit `Resources.resw` by anything able to - from Notepad, Visual Studio's editor, to tools found on GitHub.

For `Miscs.xaml`, since it's not resw, editing it is a bit different. But the main task is to **replace strings**. Don't do anything else.

As of comments, you can translate/add/remove, while making/keeping each string understandable.