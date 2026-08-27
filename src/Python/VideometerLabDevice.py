import serial
import time

# Timeout in seconds when reading command responses over the serial connection
defaultReadTimeout = 3 
# Timeout in seconds when writing commands over the serial connection
defaultWriteTimeout = 3
# The number of times to try and connect to the VideometerLab instrument and check for the "is ready" status
initializeRetries = 3
# The time in seconds to wait in between trying to connect to the VideometerLab instrument and check for the "is ready" status
secondsToWaitBetweenInitializeRetries = 5
# The maximum number of times ot try and resend a command if the sending of the command fails or if the response is not the expected.
sendCommandMaxRetries = 3
# The com port to connect to. I.e. the com port that the VideometerLab instrument is connected to.
comPort = 'COM1'

class VideometerLabDevice(object):
    def __init__(self):
        self.port = comPort
        self.baud = 9600
        self.databits = serial.EIGHTBITS
        self.parity = serial.PARITY_NONE
        self.stopbits = serial.STOPBITS_ONE
        self.ser = None
        
    def Initialize(self):
        self.ser = serial.Serial(self.port,
                                 self.baud,
                                 bytesize=self.databits,
                                 parity=self.parity,
                                 stopbits=self.stopbits,
                                 timeout=defaultReadTimeout,
                                 write_timeout=defaultWriteTimeout)
        self.ser.close()
        self.ser.open()
        
        self.ser.reset_input_buffer()
        self.ser.reset_output_buffer()       
        
        nFailes = 0
        while nFailes < initializeRetries:
            try:
                # Check the connection to the VideometerLab instrument
                self.SendCommandWithRetry("CheckConnection", "ConnectionOK", defaultReadTimeout)
                return          
            except:
                print(f"Failed to connect to the VideometerLab instrument. Trying again in {secondsToWaitBetweenInitializeRetries} seconds.")
                nFailes = nFailes + 1
                time.sleep(secondsToWaitBetweenInitializeRetries)
        
        raise Exception("Failed to connect to the VideometerLab instrument.")
        
    def SendCommand(self, command, expectedResult, readTimeout):
        try:
            self.ser.write(str.encode(command + '\n'))
        except:
            print(f"Failed to send command {command}.")
            raise Exception(f"Failed to send command {command}.")
        
        self.ser.timeout = readTimeout
        read = self.ser.readline().decode().strip()
        commandResponseOK = read == expectedResult
        
        if commandResponseOK == False:
            error_message = f"Failed to get expected response from command {command}. Expected {expectedResult}, but received {read}."
            error_message_joined = ''.join(error_message)
            print(error_message_joined)
            error_message_string = ''.join(map(str, error_message))
            raise Exception("{}".format(error_message))

    def SendCommandWithRetry(self, command, expectedResult, readTimeout, maxAttempts=sendCommandMaxRetries):
        attempt = 0
        while attempt < maxAttempts:
            try:
                self.SendCommand(command, expectedResult, readTimeout)
                return  # Command succeeded, no need to retry
            except Exception as e:
                attempt += 1
                if attempt < maxAttempts:
                    print(f"Retrying command {command}")
                else:
                    print(f"Maximum attempts reached. Giving up on command {command}.")
                    e.args = f"Maximum attempts reached. Giving up on command {command}. {''.join(e.args)}"
                    raise
        
    def CaptureImage(self, sampleId, initials, comments, captureImageTimeoutSeconds):
        # In case the capture do not finish in time, allow for a small amount of slack to have time to read the correct
        # error message over the serial connection instead of just throwing a timeout.
        captureImageTimeoutSeconds = captureImageTimeoutSeconds + 1
        commandWithParameters = f"Capture;{sampleId};{initials};{comments};{captureImageTimeoutSeconds}";
        self.SendCommandWithRetry(commandWithParameters, "CaptureFinish", captureImageTimeoutSeconds)
                
    def WaitForAnalysisComplete(self, analysisTimeoutSeconds):
        # In case the analysis do not finish in time, allow for a small amount of slack to have time to read the correct
        # error message over the serial connection instead of just throwing a timeout.
        analysisTimeoutSeconds = analysisTimeoutSeconds + 1 
        commandWithParameters = f"WaitForAnalysisComplete;{analysisTimeoutSeconds}";
        self.SendCommandWithRetry(commandWithParameters, "AnalysisComplete", analysisTimeoutSeconds)
        
    # Returns when the sphere is up           
    def WaitForSphereUp(self, sphereUpTimeoutSeconds):
        commandWithParameters = f"WaitForSphereUp;{sphereUpTimeoutSeconds}";
        self.SendCommandWithRetry(commandWithParameters, "SphereIsUp", sphereUpTimeoutSeconds)
           
    # If the analysis of the last image failed then an error message with detailes is thrown as an exception
    def CheckIfLastImageFailed(self):
        self.SendCommandWithRetry("LastImageFailed", "False", defaultReadTimeout)

    # Loads the named light setup into the instrument.
    # The light setup name must not contain the ';' character, as that separates the command parameters.
    def LoadLightSetup(self, lightSetupName, loadLightSetupTimeoutSeconds):
        # In case the load do not finish in time, allow for a small amount of slack to have time to read the correct
        # error message over the serial connection instead of just throwing a timeout.
        loadLightSetupTimeoutSeconds = loadLightSetupTimeoutSeconds + 1
        commandWithParameters = f"LoadLightSetup;{lightSetupName};{loadLightSetupTimeoutSeconds}";
        self.SendCommandWithRetry(commandWithParameters, "LightSetupLoaded", loadLightSetupTimeoutSeconds)

    # Saves the light setup currently used by the instrument under the given name.
    # The light setup name must not contain the ';' character, as that separates the command parameters.
    def SaveLightSetup(self, lightSetupName, saveLightSetupTimeoutSeconds):
        # In case the save do not finish in time, allow for a small amount of slack to have time to read the correct
        # error message over the serial connection instead of just throwing a timeout.
        saveLightSetupTimeoutSeconds = saveLightSetupTimeoutSeconds + 1
        commandWithParameters = f"SaveLightSetup;{lightSetupName};{saveLightSetupTimeoutSeconds}";
        self.SendCommandWithRetry(commandWithParameters, "LightSetupSaved", saveLightSetupTimeoutSeconds)

    # Clears a failed light setup adjustment so the instrument accepts commands again.
    # A failed LoadLightSetup, SaveLightSetup or DoAutoLight leaves the instrument refusing further commands
    # until this is called, so images cannot go on being captured without the light setup that never took effect.
    def AcknowledgeAdjustingLightSetupFailed(self):
        self.SendCommandWithRetry("AcknowledgeAdjustingLightSetupFailed", "AdjustingLightSetupFailedAcknowledged",
                                  defaultReadTimeout)

    # Optimizes the light setup for the sample currently placed under the sphere, and applies the result.
    # Place the sample before calling this, as the optimization measures it.
    def DoAutoLight(self, autoLightTimeoutSeconds):
        # In case the auto light do not finish in time, allow for a small amount of slack to have time to read the
        # correct error message over the serial connection instead of just throwing a timeout.
        autoLightTimeoutSeconds = autoLightTimeoutSeconds + 1
        commandWithParameters = f"DoAutoLight;{autoLightTimeoutSeconds}";
        self.SendCommandWithRetry(commandWithParameters, "AutoLightComplete", autoLightTimeoutSeconds)
        
        