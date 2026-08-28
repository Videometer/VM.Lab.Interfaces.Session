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
# How much longer than the instrument's own wait to keep reading for its response.
# Commands that carry a timeout are answered by the instrument when that timeout runs out, so without this aditional
# wait time the two deadlines coincide: the read can expire a moment before the answer arrives, which hides the real
# reason and — because an empty read counts as retryable — resends the command into a session that is still busy.
secondsToReadBeyondTheInstrumentsTimeout = 1
# The prefix the instrument puts on a response that reports the outcome of a command it did receive and
# understand. Such a command is not retried, because sending it again would only repeat the outcome.
# Matched without the trailing space the instrument sends, so the space stays cosmetic.
commandFailedPrefix = 'FAILED:'

class VideometerLabCommandFailed(Exception):
    """
    Raised when the instrument reports the outcome of a command it did receive and understand, and that
    outcome is not success. The command is not retried, because sending it again would only repeat the outcome.
    Recognised by the commandFailedPrefix on the response, so that this is kept apart from the instrument
    answering that it could not read what arrived — which can be link noise, and is retried.
    """


class VideometerLabDevice(object):
    def __init__(self):
        self.port = comPort
        self.baud = 9600
        self.databits = serial.EIGHTBITS
        self.parity = serial.PARITY_NONE
        self.stopbits = serial.STOPBITS_ONE
        self.ser = None
        
    def Initialize(self):
        # Passing port= to the constructor already opens the port, so there is no close and reopen here.
        self.ser = serial.Serial(self.port,
                                 self.baud,
                                 bytesize=self.databits,
                                 parity=self.parity,
                                 stopbits=self.stopbits,
                                 timeout=defaultReadTimeout,
                                 write_timeout=defaultWriteTimeout)

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

    # Closes the com port. Call this when done with the instrument, ideally from a finally block so it also
    # runs when a command fails.
    # Leaving it to the garbage collector instead fails: it finalizes the port during interpreter shutdown,
    # after the modules pyserial needs have been torn down, which prints
    # "Exception ignored while finalizing file Serial<...>: TypeError: 'NoneType' object is not callable".
    # It also leaves the moment the port closes up to chance, which makes the next script's open more likely
    # to corrupt its first byte.
    def CloseComPort(self):
        if self.ser is not None and self.ser.is_open:
            self.ser.close()

    def SendCommand(self, command, expectedResult, readTimeout):
        # Set the read timeout before writing, and only when it actually differs.
        # Assigning timeout makes pyserial reconfigure the open port (SetCommTimeouts + SetCommState, which
        # re-applies the whole DCB). Doing that straight after a write corrupts the byte still being
        # transmitted, so the instrument receives a mangled first character and rejects the whole command.
        # The guard matters because pyserial reconfigures on every assignment, even to the same value.
        if self.ser.timeout != readTimeout:
            self.ser.timeout = readTimeout

        try:
            self.ser.write(str.encode(command + '\n'))
            # Wait for the command to actually leave the port before reading the response.
            self.ser.flush()
        except:
            print(f"Failed to send command {command}.")
            raise Exception(f"Failed to send command {command}.")

        read = self.ser.readline().decode().strip()

        if read == expectedResult:
            return

        if read == "":
            error_message = f"No response from command {command} within {readTimeout} seconds."
        else:
            error_message = f"Failed to get expected response from command {command}. Expected {expectedResult}, but received {read}."
        print(error_message)

        if read.startswith(commandFailedPrefix):
            # The instrument received and understood the command and is reporting its outcome, so there iscl
            # nothing to gain by sending it again.
            raise VideometerLabCommandFailed(error_message)

        # Either nothing came back within the timeout, or the instrument answered that it could not read what
        # arrived. Both can be caused by noise on the link, so the command is worth another attempt.
        raise Exception(error_message)

    def SendCommandWithRetry(self, command, expectedResult, readTimeout, maxAttempts=sendCommandMaxRetries):
        attempt = 0
        while attempt < maxAttempts:
            try:
                self.SendCommand(command, expectedResult, readTimeout)
                return  # Command succeeded, no need to retry
            except VideometerLabCommandFailed:
                # The instrument gave a definite answer, so do not send the command again.
                raise
            except Exception as e:
                attempt += 1
                if attempt < maxAttempts:
                    print(f"Retrying command {command}")
                else:
                    print(f"Maximum attempts reached. Giving up on command {command}.")
                    e.args = f"Maximum attempts reached. Giving up on command {command}. {''.join(e.args)}"
                    raise

    def CaptureImage(self, sampleId, initials, comments, captureImageTimeoutSeconds):
        commandWithParameters = f"Capture;{sampleId};{initials};{comments};{captureImageTimeoutSeconds}";
        self.SendCommandWithRetry(commandWithParameters, "CaptureFinish",
                                  captureImageTimeoutSeconds + secondsToReadBeyondTheInstrumentsTimeout)

    def WaitForAnalysisComplete(self, analysisTimeoutSeconds):
        commandWithParameters = f"WaitForAnalysisComplete;{analysisTimeoutSeconds}";
        self.SendCommandWithRetry(commandWithParameters, "AnalysisComplete",
                                  analysisTimeoutSeconds + secondsToReadBeyondTheInstrumentsTimeout)

    # Returns when the sphere is up
    def WaitForSphereUp(self, sphereUpTimeoutSeconds):
        commandWithParameters = f"WaitForSphereUp;{sphereUpTimeoutSeconds}";
        self.SendCommandWithRetry(commandWithParameters, "SphereIsUp",
                                  sphereUpTimeoutSeconds + secondsToReadBeyondTheInstrumentsTimeout)
           
    # If the analysis of the last image failed then an error message with detailes is thrown as an exception
    def CheckIfLastImageFailed(self):
        self.SendCommandWithRetry("LastImageFailed", "False", defaultReadTimeout)

    # Starts a new measurement: clears the results shown by the instrument and makes it ready for a fresh
    # analysis, with the results saved to new files. Call this before the first sample of a new batch, so the
    # batch does not append to the previous one's files.
    def New(self):
        self.SendCommandWithRetry("New", "NewMeasurementStarted", defaultReadTimeout)

    # Loads the named light setup into the instrument.
    # The light setup name must not contain the ';' character, as that separates the command parameters.
    def LoadLightSetup(self, lightSetupName, loadLightSetupTimeoutSeconds):
        commandWithParameters = f"LoadLightSetup;{lightSetupName};{loadLightSetupTimeoutSeconds}";
        self.SendCommandWithRetry(commandWithParameters, "LightSetupLoaded",
                                  loadLightSetupTimeoutSeconds + secondsToReadBeyondTheInstrumentsTimeout)

    # Saves the light setup currently used by the instrument under the given name.
    # The light setup name must not contain the ';' character, as that separates the command parameters.
    def SaveLightSetup(self, lightSetupName, saveLightSetupTimeoutSeconds):
        commandWithParameters = f"SaveLightSetup;{lightSetupName};{saveLightSetupTimeoutSeconds}";
        self.SendCommandWithRetry(commandWithParameters, "LightSetupSaved",
                                  saveLightSetupTimeoutSeconds + secondsToReadBeyondTheInstrumentsTimeout)

    # Clears a failed light setup adjustment so the instrument accepts commands again.
    # A failed LoadLightSetup, SaveLightSetup or DoAutoLight leaves the instrument refusing further commands
    # until this is called, so images cannot go on being captured without the light setup that never took effect.
    def AcknowledgeAdjustingLightSetupFailed(self):
        self.SendCommandWithRetry("AcknowledgeAdjustingLightSetupFailed", "AdjustingLightSetupFailedAcknowledged",
                                  defaultReadTimeout)

    # Optimizes the light setup for the sample currently placed under the sphere, and applies the result.
    # Place the sample before calling this, as the optimization measures it.
    def DoAutoLight(self, autoLightTimeoutSeconds):
        commandWithParameters = f"DoAutoLight;{autoLightTimeoutSeconds}";
        self.SendCommandWithRetry(commandWithParameters, "AutoLightComplete",
                                  autoLightTimeoutSeconds + secondsToReadBeyondTheInstrumentsTimeout)
        
        