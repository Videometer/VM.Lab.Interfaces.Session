This document is split into two sections.
* The first section is relevant for VideometerLab version 4.3 and newer.
* [The second section is relevant for VideometerLab version 4.3 and older.](#the-below-is-relevant-for-videometerlab-version-43-and-older)

# The below is relevant for VideometerLab version 4.3 and newer

# Communication interface used for controlling VideometerLab Session

<!-- TOC -->
* [Overview](#overview)
* [Generic C# controller for Session](#generic-c-controller-for-session)
  * [Capture](#capture)
  * [New](#new)
  * [Stop](#stop)
  * [LoadLightSetup](#loadlightsetup)
  * [SaveLightSetup](#savelightsetup)
  * [DoAutoLight](#doautolight)
  * [AcknowledgeAdjustingLightSetupFailed](#acknowledgeadjustinglightsetupfailed)
  * [StateChanged](#statechanged)
  * [ProvideErrorMessage](#provideerrormessage)
* [Python implementation for controlling Session](#python-implementation-for-controlling-session)
  * [Initialize](#initialize)
  * [Capture Image](#capture-image)
  * [Wait For Analysis Complete](#wait-for-analysis-complete)
  * [Wait For Sphere Up](#wait-for-sphere-up)
  * [Check If Last Image Failed](#check-if-last-image-failed)
  * [LoadLightSetup](#loadlightsetup-1)
  * [SaveLightSetup](#savelightsetup-1)
  * [DoAutoLight](#doautolight-1)
* [Python scripts](#python-scripts)
  * [VideometerLabDevice.py](#videometerlabdevicepy)
  * [CaptureImage.py](#captureimagepy)
  * [WaitForSphereUp.py](#waitforsphereuppy)
  * [DidLastImageFail.py](#didlastimagefailpy)
  * [LoadLightSetup.py](#loadlightsetuppy)
  * [SaveLightSetup.py](#savelightsetuppy)
  * [DoAutoLight.py](#doautolightpy)
  * [Suggested User Flow](#suggested-user-flow)
* [REST API implementation for controlling Session](#rest-api-implementation-for-controlling-session)
  * [Endpoints](#endpoints)
  * [Responses](#responses)
  * [Example](#example)
* [Technical details](#technical-details)
  * [Setup for making and using your own C# controller](#setup-for-making-and-using-your-own-c-controller)
  * [Setup for use of the REST API controller](#setup-for-use-of-the-rest-api-controller)
  * [Setup for use of a Python controller](#setup-for-use-of-a-python-controller)
  * [Setup for making your own Python controller](#setup-for-making-your-own-python-controller)
  * [Session state machine](#session-state-machine)
<!-- TOC -->

# Overview
The communication protocol handles messages between the VideometerLab Session and an external controller. The external controller can for instance be a PLC, custom C# application, or custom Python scripts.

Videometer provides the following:
* An abstract C# class "ExternalSessionController" which anyone can inherit from and make a custom implementation.
* A C# implementation called "ExternalSerialSessionController" that communicates over a serial connection with Phyton scripts that can control the Session.
  * Corresponding Phyton scripts to control the Session.
* A C# implementation called "ExternalRestSessionController" that exposes a REST API over HTTP that can control the Session.

All implementations work by controlling the Session by calling methods on the "IExternalSessionControllerListener" interface.

# Generic C# controller for Session
Below is listed the methods that are exposed by the generic C# controller that can be used to control Session from C# code.

## Capture
An image is captured, analyzed, and the result is shown on screen. This corresponds to using the capture button in the GUI.
The command takes the following arguments:
* Sample ID
* Operator initials
* Comment

## New
Cleares the GUI and makes ready for beginning new measurements. There are no arguments to this method.

## Stop
Stops the current measurement. This corresponds to using the stop button in the GUI. There are no arguments to this method.

## LoadLightSetup
Loads a specified light setup into the device. The name of the light setup is passed as an argument. The light setup must be saved in the active workspace.

## SaveLightSetup
Saves the active light setup to the active workspace. The name of the light setup is passed as an argument.

## DoAutoLight
Performs an automatic light setup. There are no arguments to this method.

## AcknowledgeAdjustingLightSetupFailed
Acknowledge a failed light setup adjustment.
The session itself does not clear a failed light setup adjustment: it leaves the session in *SessionState.AdjustingLightSetupFailed*, where it refuses further commands, until the controller that issued the adjustment acknowledges the failure. That way the external controller cannot continue to capture images without being aware of the light setup adjustment failure.

## StateChanged
This method is called when ever the internal Session state changes. See diagram below.

## ProvideErrorMessage
Called if an error happens, for example, something in the pipeline of capturing, analyzing, or saving of results, failed for the current image. Used to inform external controllers that something went wrong.

# Python implementation for controlling Session

This section explains an example of an external Session controller written in Python. The controller consists of two parts:
* A C# implementation called "ExternalSerialSessionController"
* Python scripts

It works by communicating over a serial connection between the "ExternalSerialSessionController" and the Python scripts.    
Both the "ExternalSerialSessionController" and the Python scripts can be edited and customized to your needs/application.
Both is supplied in the Releases section.

## Initialize
This method initializes the connection between the Python scripts and the VideometerLab instrument. If a connection can not be established it is retried a number of times. This behavior cna be controlled by parameters in the top of the file VideometerLabDevice.py.

## Capture Image
This method tells the VideometerLab instrument to capture an image. This image is analysed according to the loaded recipe. This method returns once the image have been captured and before the analysis begins.

## Wait For Analysis Complete
This method waits for the current analysis to finish. If the VideometerLab instrument is currently analyzing an image, then this method waits for this analysis to finish. If the VideometerLab instrument is not currently analyzing an image, this method returns immediately.  
A timeout parameter is provided that specifies the maximum time this method is allowed to wait before throwing an exception.

## Wait For Sphere Up
This method waits until the VideometerLab sphere has moved all the way up. If the VideometerLab sphere is already in its up position, then this method returns immediately.  
A timeout parameter is provided that specifies the maximum time this method is allowed to wait before throwing an exception.

## Check If Last Image Failed
This method asks the VideometerLab software if the analysis of the previous image was successful or not. If the analysis of the previous image failed, an exception is throw containing an error message. This error message is identical to the error message shown in the VideometerLab software. This method can be used to make automatic decisions on how to handle analysis errors based on the individual error message.

## LoadLightSetup
Loads a specified light setup into the device. The name of the light setup is passed as an argument. The light setup must be saved in the active workspace.

## SaveLightSetup
Saves the active light setup to the active workspace. The name of the light setup is passed as an argument.

## DoAutoLight
Performs an automatic light setup. There are no arguments to this method.

# Python scripts
Videometer provides a Python class called VideometerLabDevice as well as scripts that use this class. These scripts are provided open-source, free of charge, and the user can freely customize them to their specific needs.

## VideometerLabDevice.py
This file contains a Python implementation that can be used to control the VideometerLab instrument. All variables are defined at the top of the file, and it is not generally necessary to modify this file. All below scripts use the VideometerLabDevice class defined in this file.

## CaptureImage.py
This script do the following:
* Connected to the VideometerLab instrument
* Waited for any ongoing analysis to finish
* Captures a new image and starts the analysis according to the loaded recipe
* Waits for the VideometerLab sphere to move all the way up

## WaitForSphereUp.py
This script does the following:
* Connected to the VideometerLab instrument
* Waits for the VideometerLab sphere to move all the way up

If the VideometerLab sphere is already all the way up then this script immediately.

## DidLastImageFail.py
This script does the following:
* Connected to the VideometerLab instrument
* Checks if the analysis of the previous image was successful or not.

If the analysis of the previous image failed an exception is throw containing an error message. This error message is identical to the error message shown in the VideometerLab software. This method can be used to make automatic decisions on how to handle analysis errors based on the individual error message.

## LoadLightSetup.py
This script does the following:
* Connected to the VideometerLab instrument
* Loads a specified light setup into the device.

## SaveLightSetup.py
This script does the following:
* Connected to the VideometerLab instrument
* Saves the active light setup to the active workspace

## DoAutoLight.py
This script does the following:
* Connected to the VideometerLab instrument
* Performs an automatic light setup.

## Suggested User Flow
The Python scripts are written for an application where a robotic system is placing presenting samples to the VideometerLab instrument. This robotic system can execute the Python scripts as it means of communication with the VideometerLab instrument.  
The suggested user flow is as follows:
1. The human operator manually starts the VideometerLab Session and loads the necessary recipe
2. Before the robot presents the first sample to the VideometerLab instrument, it calls the Python script "WaitForSphereUp.py" to ensure the VideometerLab sphere is up and thereby that the robot does not collide with the sphere.
3. The robot presents a sample in the VideometerLab instrument and moves away so the sphere can move down without colliding with the robot arm.
4. The robot calls the Python script "CaptureImage.py" to capture an image and start the analysis. Once the script returns, the robot can safely remove the sample again.
5. Repeat from step 3.

Optionally, a call to the Python script "DidLastImageFail.py" can be added between step 4 and 5 and any custom error handling be done here.

# REST API implementation for controlling Session

The "ExternalRestSessionController" exposes the same commands as the serial implementation, as a REST API over HTTP. Any client that can send HTTP requests can control the Session.

* Base address: `http://<VideometerLab PC>:5080/api/v1`
* Request and response bodies are JSON.
* The API listens on all network interfaces and has no authentication and no encryption. Anyone who can reach the port can control the instrument, so only use it on a trusted network.
* A POST without a body must still send the header `Content-Length: 0`, or Windows rejects it with status code 411. Most HTTP clients do this by themselves; with curl, add `-d ""`.
* Commands that take time answer when the step has finished, failed or timed out. Set the HTTP client's timeout longer than the `timeoutSeconds` sent in the request.
* Only one of the commands Capture, New and the light setup commands is processed at a time. Stop, the waits and the queries can be called while such a command is in progress.

## Endpoints

| Method | Path                            | Body                                           | Success message                       |
|--------|---------------------------------|------------------------------------------------|---------------------------------------|
| GET    | /connection                     |                                                | ConnectionOK                          |
| GET    | /state                          |                                                | `{"state": "Ready"}`                  |
| GET    | /last-image                     |                                                | `{"failed": false, "errorMessage": null}` |
| POST   | /capture                        | `id`, `initials`, `comments`, `timeoutSeconds` | CaptureFinish                         |
| POST   | /wait-for-analysis-complete     | `timeoutSeconds`                               | AnalysisComplete                      |
| POST   | /wait-for-sphere-up             | `timeoutSeconds`                               | SphereIsUp                            |
| POST   | /new                            |                                                | NewMeasurementStarted                 |
| POST   | /stop                           |                                                | Stopped                               |
| POST   | /light-setup/load               | `lightSetupName`, `timeoutSeconds`             | LightSetupLoaded                      |
| POST   | /light-setup/save               | `lightSetupName`, `timeoutSeconds`             | LightSetupSaved                       |
| POST   | /light-setup/auto               | `timeoutSeconds`                               | AutoLightComplete                     |
| POST   | /light-setup/acknowledge-failed |                                                | AdjustingLightSetupFailedAcknowledged |

/capture answers once the image has been captured and before the analysis has finished, like the Python method "Capture Image".

## Responses

Every command answers with `{"success": true|false, "message": "..."}`.

| Status code | Meaning                                                                                   |
|-------------|-------------------------------------------------------------------------------------------|
| 200         | The command succeeded.                                                                    |
| 400         | The request is invalid, for example a missing body or `timeoutSeconds`.                   |
| 403         | The request came from another PC, and the API is not set up for use from the network. See "Setup for use of the REST API controller". |
| 409         | The Session refused the command, or another command is still being processed.             |
| 500         | The step was started but failed. The message holds the error message from the Session.    |
| 503         | The sphere height is not available.                                                       |
| 504         | The step did not finish within `timeoutSeconds`. For /capture and the light setup commands this is also the answer if the Session never started the step. |

Failures are kept apart by kind:
* A failed image is answered by /capture and /wait-for-analysis-complete, and is what GET /last-image reports. It is cleared by the next /capture.
* A failed light setup adjustment is answered by the light setup command that caused it. It is not reported by GET /last-image or by /wait-for-analysis-complete. The Session stays in the state `AdjustingLightSetupFailed` until /light-setup/acknowledge-failed is called.

## Example

```
curl -X POST http://localhost:5080/api/v1/capture -H "Content-Type: application/json" -d "{\"id\":\"Sample1\",\"initials\":\"AB\",\"comments\":\"\",\"timeoutSeconds\":30}"
```

# Technical details

## Setup for making and using your own C# controller
To make your own session controller in C# you have to make a new class and inherent from the abstract class "ExternalSessionController". You then compile your dll and copy it into the VideometerLab installation folder. When you start Session, your external Session controller can now be used.  
An example of this can be found in the "ExternalSerialSessionController" class.

## Setup for use of the REST API controller
To use the REST API controller, you need to copy the dll "VM.Lab.Session.ExternalRestSessionController.dll" into the VideometerLab installation folder. When you start Session you can now send commands to the VideometerLab instrument as HTTP requests.

Note that:
* Without the two steps below, the API only answers requests sent from the VideometerLab PC itself, to `http://localhost:5080/api/v1`.
* To reach the API from another PC, Windows must be told that Session may listen on the network. Run this once as administrator on the VideometerLab PC, and restart Session:
  ```
  netsh http add urlacl url=http://+:5080/api/v1/ sddl="D:(A;;GX;;;WD)"
  ```
* To reach the API from another PC, inbound TCP port 5080 must also be allowed in the Windows firewall on the VideometerLab PC.
* The port number is defined at the top of the "ExternalRestSessionController" class.

## Setup for use of a Python controller
To use our Python controller, you need to copy the dll "VM.Lab.Session.ExternalSerialSessionController.dll" into the VideometerLab installation folder. When you start Session you can now send commands to the VideometerLab instrument by executing the provided Python scripts.

Note that:
* The Python scripts assume that the VideometerLab PC and the external PC are connected by a serial cable.
* You will need to adjust the COM port number at the top of the file "VideometerLabDevice.py" to be the COM port number of the VideometerLab device.
* You will need to change the COM port number assigned by Windows to COM2 for the COM port on the PC that is connected to the external controller.

The assigned COM port number is changed using these steps:
1) Open the Device Manager (Can be done by pressing the Windows Key + R. Type “devmgmt.msc” and press Enter.)
2) Expand the Ports (COM & LPT) section
3) Right-click the COM port and select Properties
4) Click the Port Settings tab
5) Click Advanced
6) Change the COM port number in the dropdown
7) Click OK
8) Click OK
9) If the COM port number is already in use restart your computer after following the above steps

![](How_to_Change_COM_Port_Number_on_Windows.png)

## Setup for making your own Python controller
You can customize our Python scripts to your liking. To make your own Session controller for Python, you can either:
* Use the "VM.Lab.Session.ExternalSerialSessionController.dll" as provided, but modify the Python scripts to your needs.
* Change the source code for the "ExternalSerialSessionController" class and modify the Python scripts to your needs.

You will need to import the Python library's "serial" and "time" to modify the VideometerLabDevice class.

## Session state machine
The diagram below slows the Session state machine. These states can be used for custom implementation of the Session controllers.

![](Session-StateMachineVisualizationGraph-01-September-2026.svg)


# The below is relevant for VideometerLab version 4.3 and older

# Communication interface used for controlling VideometerLab Session
<!-- TOC -->
* [Overview](#overview)
* [Generic C# controller for Session](#generic-c-controller-for-session)
  * [Start](#start)
  * [Pause](#pause)
  * [Finish](#finish)
  * [New](#new)
  * [State Changed](#state-changed)
  * [Last Image Failed](#last-image-failed)
* [Python implementation for controlling Session](#python-implementation-for-controlling-session)
  * [Initialize](#initialize)
  * [Capture Image](#capture-image)
  * [Wait For Analysis Complete](#wait-for-analysis-complete)
  * [Wait For Sphere Up](#wait-for-sphere-up)
  * [Check If Last Image Failed](#check-if-last-image-failed)
* [Python scripts](#python-scripts)
  * [VideometerLabDevice.py](#videometerlabdevicepy)
  * [CaptureImage.py](#captureimagepy)
  * [WaitForSphereUp.py](#waitforsphereuppy)
  * [DidLastImageFail.py](#didlastimagefailpy)
  * [Suggested User Flow](#suggested-user-flow)
* [Technical details](#technical-details)
  * [Setup for making and using your own C# controller](#setup-for-making-and-using-your-own-c-controller)
  * [Setup for use of Python controller](#setup-for-use-of-python-controller)
  * [Setup for making your own Python controller](#setup-for-making-your-own-python-controller)
  * [Session state machine](#session-state-machine)
<!-- TOC -->

# Overview
The communication protocol handles messages between the VideometerLab Session and an external controller. The external controller can for instance be a PLC, custom C# application, or custom Python scripts.

Videometer provides the following:
* An abstract C# class "SessionController" which anyone can inherit from and make a custom implementation.
* A C# implementation called "SerialSessionController" that communicates over a serial connection with Phyton scripts that can control the Session.
  * Corresponding Phyton scripts to control the Session.

Both implementations work by controlling the Session by calling methods on the "ISessionControllerListener" interface.

# Generic C# controller for Session
Below is listed the methods that are exposed by the generic C# controller that can be used to control Session from C# code.

## Start
Starts a new measurement. This corresponds to using the play/start button in the GUI. An image is captured, analysed and the result is shown on screen.
The command takes the following arguments:
* Sample Id
* Operator initials
* Comment
* Boolean indicating whether to suffix by timestamp

## Pause
Pauses the current measurement. This corresponds to using the pause button in the GUI. There are no arguments to this method.

## Finish
Finishes the current measurement. This corresponds to using the finish button in the GUI. There are no arguments to this method.

## New
Cleares the GUI and makes ready for beginning new measurements. This corresponds to using the new button in the GUI. There are no arguments to this method.

## State Changed
This method is called when ever the internal Session state changes. See diagram below.

## Last Image Failed
This method is called if the capture or analysis of an image fails. This can be used to transfer the error message to the external controller and have the external controller decide how to handle the error.

# Python implementation for controlling Session

This section explains an example of an external Session controller written in Python. The controller consists of two parts:
* A C# implementation called "SerialSessionController"
* Python scripts

It works by communicating over a serial connection between the "SerialSessionController" and the Python scripts.    
Both the "SerialSessionController" and the Python scripts can be edited and customized to your needs/application.
Both is supplied in the Releases section.


## Initialize
This method initializes the connection between the Python scripts and the VideometerLab instrument. If a connection can not be established it is retried a number of times. This behavior cna be controlled by parameters in the top of the file VideometerLabDevice.py.

## Capture Image
This method tells the VideometerLab instrument to capture an image. This image is analysed according to the loaded recipe. This method returns once the image have been captured and before the analysis begins.

## Wait For Analysis Complete
This method waits for the current analysis ot finish. If the VideometerLab instrument is currently analysing an image then this method waits for this analysis to finish. If the VideometerLab instrument is not currently analysing  an image this method returns immediately.  
A timeout parameter is provided the specifies the maximum time this method is allowed ot wait before throwing an exception.

## Wait For Sphere Up
This method waits until the VideometerLab sphere have moved all the way up. If the VideometerLab sphere is already in its up position then this method returns immediately.  
A timeout parameter is provided the specifies the maximum time this method is allowed ot wait before throwing an exception.

## Check If Last Image Failed
This method askes the VideometerLab software if the analysis of the previous image was successful or not. If the analysis of the previous image failed an exception is throw containing an error message. This error message is identical to the error message shown in the VideometerLab software. This method can be used to make automatic decisions on how to handle analysis errors based on the individual error message.

# Python scripts
Videometer provides a Python class called VideometerLabDevice as well as scripts that use this class. These scripts are provided open-source, free of charge and the user can freely customize them to their specific needs.

## VideometerLabDevice.py
This file contains a Python implementation that can be used to control the VideometerLab instrument. All variables are defined at the top of the file, and it is not generally needed to modify this file. All below scripts uses the VideometerLabDevice class defined in this file.

## CaptureImage.py
This script do the following:
* Connected to the VideometerLab instrument
* Waited for any ongoing analysis to finish
* Captures a new image and starts the analysis according to the loaded recipe
* Waits for the VideometerLab sphere to move all the way up

## WaitForSphereUp.py
This script do the following:
* Connected to the VideometerLab instrument
* Waits for the VideometerLab sphere to move all the way up

If the VideometerLab sphere is already all the way up then this script immediately.

## DidLastImageFail.py
This script do the following:
* Connected to the VideometerLab instrument
* Checks if the analysis of the previous image was successful or not.

If the analysis of the previous image failed an exception is throw containing an error message. This error message is identical to the error message shown in the VideometerLab software. This method can be used to make automatic decisions on how to handle analysis errors based on the individual error message. 

## Suggested User Flow
The Python scripts is written for an application where a robotic system is placing presenting samples to the VideometerLab instrument. This robotic system can execute the Python scripts as it means of communication with the VideometerLab instrument.  
The suggested user flow is as follows:
1. The human operator manually starte the Videometer Session and loaded the needed recipe
2. Before the robot presents the first sample to the VideometerLab instrument it calls the Python script "WaitForSphereUp.py" to ensure the VideometerLab sphere is up and thereby that the robot do not collide with the sphere.
3. The robot presents a sample in the VideometerLab instrument and moves away so the sphere can move down without colliding with the robot arm.
4. The robot calls the Python script "CaptureImage.py" to capture an image and start the analysis. Once the script returns the robot can safely remove the sample again.
5. Repeat from step 3.

Optionally a call to the Python script "DidLastImageFail.py" can be added between step 4 and 5 and any custom error handling be done here.

# Technical details

## Setup for making and using your own C# controller
In order to use make your own session controller in C# you simply have to make a new class and inherent from the abstract class "SessionController". You then compile your dll and copy it into the VideometerLab installation folder. When you start Session your external Session controller can now be used.  
An example of this can be found in the "SerialSessionController" class.

## Setup for use of Python controller
In order to use our Python controller you need to copy the dll "VM.Lab.Session.SerialSessionController.dll" into the VideometerLab installation folder. When you start Session you can now send commands to the VideometerLab instrument by executing the provided Python scripts.

Note that: 
* The Python scripts assume that the VideometerLab PC and the external PC is connected by a serial cable.
* You will need to adjust the COM port number in the top of the file "VideometerLabDevice.py" to be the COM port number of the VideometerLab device.
* You will need to change the COM port number assigned by Windows to COM2 for the COM port on the PC that is connected to the external controller.

The assigned COM port number is changed using these steps:
1) Open the Device Manager (Can be done by pressing the Windows Key + R. Type “devmgmt.msc” and press Enter.)
2) Expand the Ports (COM & LPT) section
3) Right-click the COM port and select Properties
4) Click the Port Settings tab
5) Click Advanced
6) Change the COM port number in the dropdown
7) Click OK
8) Click OK
9) If the COM port number is already in use restart your computer after following the above steps

![](How_to_Change_COM_Port_Number_on_Windows.png)

## Setup for making your own Python controller
You can customize our Python scripts to your liking. In order to make your own Session controller for Python you can either:
* Simply use the "VM.Lab.Session.SerialSessionController.dll" as provided, but modify the Python scripts to your needs.
* Change the source code for the "SerialSessionController" class and modify the Python scripts to your needs.

You will need to import the Python library's "serial" and "time" to modify the VideometerLabDevice class.

## Session state machine
The diagram below slows the Session state machine. These state can be used for custom implementation of the Session controllers.

![](Session_StateMachine_Old.png)
