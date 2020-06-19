# Potato.Fastboot
[![NuGet Version](https://img.shields.io/nuget/v/Potato.Fastboot.svg)](https://www.nuget.org/packages/Potato.Fastboot)
![GPL-3.0](https://img.shields.io/github/license/mashed-potatoes/Potato.Fastboot.svg)

A small wrapper over LibUsbDotNet for easy and convenient communication with mobile devices in Fastboot mode.


## Example

```c#
var fb = new Fastboot();

// Connect to first available device
fb.Connect();

// Get product name (generic variable)
var product = fb.Command("getvar:product"); // Example output: davinci

// Print serial number
Console.WriteLine(fb.GetSerialNumber());

// Print product name
Console.WriteLine(product.Payload);

// Upload local file with path
fb.UploadData("recovery.img");

// or with FileStream
fb.UploadData(new FileStream("recovery.img", FileMode.Open));

// Flash uploaded data to recovery partition
var res = fb.Command("flash:recovery");

// Print status
Console.WriteLine(res.Status); // Example output: Okay

// Boot uploaded ramdisk
fb.Command("boot");
```

## Short documentation

### Methods of Fastboot object

#### `Wait()`: `void`
Wait for any device, timeout: 25 seconds.

#### `Connect()`: `void`
Connect to first available device.

#### `Disconnect()`: `void`
Disconnect the device and dispose some resources.

#### `GetSerialNumber()`: `string`
Get device serial number.

#### `Command(byte[])`: `Response`
Send command (as bytes array) and read response.

#### `Command(string)`: `Response`
Send command (as simple string) and read response.

#### `UploadData(FileStream)`: `void`
Upload data from `FileStream`.

#### `UploadData(string)`: `void`
Upload data from specified file.

#### `GetDevices()`: `string[]`
Get an array of serial numbers of connected devices.

### `Response` (class)

 - Status: `Status` - status convered from header
 - Payload: `string` - response from device
 - RawData: `byte[]` - raw response

### `Status` (enum)

 - Data
 - Fail
 - Info
 - Okay
 - Unknown

# License

[GNU General Public License v3.0](LICENSE.txt)

