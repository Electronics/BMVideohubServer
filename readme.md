Dependencies and things to note:

* Needs a fix for the Makaretu.Dns module, can use this in the mean time (Add as a dependency) https://github.com/Electronics/net-mdns
* Even at the best of times, mdns can be a little funky occasionally. Check to see if advertisements are going out on the right interfaces, and/or responses are being correctly replied to
* You'll need to pull git submodules (`git clone --recurse-submodules`, or `git submodule init` then `git submodule update` if you've already cloned the repository)
