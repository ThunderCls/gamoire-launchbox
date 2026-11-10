# lib

`Unbroken.LaunchBox.Plugins.dll` is copied from LaunchBox 14.1.0.5 (`LaunchBox\Core`). It is a compile-time reference
only: the project references it with `Private=false` and it is never shipped in the plugin zip (LaunchBox loads its own).

To verify, hash your own copy and compare with `SHA256SUMS`:

```powershell
(Get-FileHash LaunchBox\Core\Unbroken.LaunchBox.Plugins.dll -Algorithm SHA256).Hash
```
