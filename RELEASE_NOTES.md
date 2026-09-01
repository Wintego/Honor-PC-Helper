## Honor PC Helper 1.8.2

Driver list accuracy:

- Installed driver versions are now read from the right device. Short keywords are matched as whole words, so "Microsoft Input Configuration Device" is no longer mistaken for the neural processor, and the Gaussian & Neural Accelerator of older platforms is no longer reported as the NPU.
- When several devices belong to one HONOR package, the version shared by most of them is reported instead of the highest one found, and vendor drivers are preferred over the generic Windows ones.
- Chipset, graphics, camera, fingerprint, monitor, Bluetooth, Wi-Fi and Smart Sound are detected by device class and hardware ID as well as by name, so localised Windows installations and AMD, NVIDIA and non-Intel models are recognised.
- A package is only announced as an update when its version can actually be compared with the installed one. Undetected components, build dates against driver versions and unrelated numbering schemes are shown for reference instead of being offered as updates that will not install.
- Package versions are read from the most detailed number in the package title, so an operating system suffix is no longer mistaken for the version.
- The log now records the detected version of every component and every offered update, which makes model-specific reports easy to check.
