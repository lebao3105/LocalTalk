# Shared project

The code used for both WP and UWP platforms.

Lemme explain stuff here:

* `Internet.cs`: HTTP(s) (and such?) and current networking information. Also do (de)serialize JSONs.
* `LocalSendProtocol.cs`: Main code for the interaction with LocalSend, or say, C# implementation of its protocol.
* `Settings.cs`: Very clear, application settings
* `Miscs.cs`: misc...stuff?