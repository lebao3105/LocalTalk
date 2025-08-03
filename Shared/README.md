# Shared project

The code used for both WP and UWP platforms.

Lemme explain stuff here:

* `Internet.cs`: HTTP(s) (and such?) and current networking information. Also do (de)serialize JSONs.
* `LocalSendProtocol.cs`: Main code for the interaction with LocalSend, or say, C# implementation of its protocol.
* `Settings.cs`: Very clear, application settings
* `Miscs.cs`: misc...stuff?

## Windows Phone compability

* `WINDOWS_PHONE` and `SILVERLIGHT` symbols are defined. Any is fit, but I prefer `WINDOWS_PHONE`.
* `WINDOWS_UWP` is defined for UWP.
* `System.Runtime.Serialization.Json` is used instead of Newtonsoft one because of Newtonsoft JSON not being able to deserialize the JSON on WP (causes Type violation or whatever I don't remember)
* `System.Text.Json` is not used because of it is not compatible with both projects. Neither Json extensions for HttpClient does.
* Keep using C# syntax that C# 6 can understand. Only the syntax.