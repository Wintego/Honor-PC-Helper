## Honor PC Helper 1.8.0

- Driver and application update checks now bypass stale localhost system proxies left behind by stopped VPN/proxy clients.
- Fixed slow and unreliable driver checks on HONOR ZQC-P/C233: the app now opens the model's official support catalog directly instead of timing out while scanning the full product tree.
- Added **Driver management**: Honor PC Helper identifies installed HONOR/OEM drivers and downloads verified packages from HONOR's official services.
- Laptop models are matched dynamically by hardware identifiers, processor and memory against HONOR's regional support catalogs; there is no hard-coded model list.
- Update-platform and support-catalog checks are independent, so one unavailable HONOR service no longer blocks the other.
- Honor PC Helper now checks its own GitHub Releases page and can replace and restart the portable exe when a newer release is available.
- Driver packages are downloaded over HTTPS. Checksums are verified when HONOR provides them, and extracted installers must have a valid Authenticode signature before they can be saved.
