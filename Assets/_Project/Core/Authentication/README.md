Create an AuthConfig-Prod file in this folder. Duplicate the existing AuthConfig-Dev.

In the Inspector, drag AuthConfig-Dev onto the Config field of your DeviceAuthManager component. Before building a production release, swap it to AuthConfig-Prod.

AuthConfig-Prod is never commited, and should be in the .gitignore
