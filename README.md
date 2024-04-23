# Communication interface used for controlling VideometerLab Session
<!-- TOC -->
* [Overview](#Overview)
* [Generic C# interface for controlling Session](#Generic-C#-interface-used-for-controlling-session)
  * [Supported commands:](#supported-commands-Generic)
      * [Capture](#Capture-Generic)
      * [Pause](#Pause-Generic)
      * [Finish](#Finish-Generic)
      * [New](#New-Generic)
      * [State Changed](#StateChanged-Generic)
      * [Last Image Failed](#Last-Image-Failed-Generic)
  * [Python implementation for controlling Session](#Python-implementation-for-controlling-session)
    * [Supported commands:](#supported-commands-Python)
      * [Check Connection](#Checkonnection-Python)
      * [Capture](#Capture-Python)
      * [Pause](#Pause-Python)
      * [Finish](#Finish-Python)
      * [New](#New-Python)
      * [Wait For Analysis Complete](#WaitForAnalysisComplete-Python)
      * [Wait For sphere up](#WaitForSphereUp-Python)
      * [Last Image Failed](#LastImageFailed-Python)
    * [Python scripts](#Python-scripts)
      * [VideometerLabDevice.py](#VideometerLabDevice)
      * [CaptureImage.py](#CaptureImage)
      * [WaitForSphereUp.py](#WaitForSphereUp)
      * [DidLastImageFail.py](#DidLastImageFail)
* [Technical details](#technical-details)
    * [Setup for use of generic C# interface](#Setup-Use-Generic)
    * [Setup for use of Python controllor](#Setup-Use-Python)
    * [Setup for making your own Python controllor](#Setup-Edit-Python)
   * [Setup for making your own C# controllor](#Setup-Edit-C#)
<!-- TOC -->

# Overview
The communication protocol handles messages between the VideometerLab Session and an external controller. The external controller can for instance be a PLC, custom C# application, or custom Python scripts.

Videometer provides the following:
* An abstract C# class "SessionController" which anyone can inherit from and make a custom implementation.
* A C# implementation called "SerialSessionController" that communicates over a serial connection with Phyton scripts that can control the Session.
  * Corresponding Phyton scripts to control the Session.

Both implementations work by controlling the Session by calling methods on the "ISessionControllerListener" interface.

# Generic C# interface for controlling Session
Below is listed the methods that are exposed by the generic C# interface that can be used to control Session from C# code.

## START
Starts a new measurement. This corresponds to using the play/start button in the GUI. An image is captured, analysed and the result is shown on screen.
```text
START|Sample Id|Operator|Comment|Suffix By Timestamp
```

## Pause
Pauses the current measurement. This corresponds to using the pause button in the GUI. There are no arguments to this method.
```text
Pause
```

## Finish
Finishes the current measurement. This corresponds to using the finish button in the GUI. There are no arguments to this method.
```text
Finish
```

## New
Cleares the GUI and makes ready for beginning new measurements. This corresponds to using the new button in the GUI. There are no arguments to this method.
```text
New
```

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





# TODO: Add image of session state machine
# TODO: Explan where to download the "SerialSessionController" dll. "A compiled version of SerialSessionController can be downloaded from ..."


