
# RPI Pico PC Temperature Display

This app can be used to display the temperature of your PCs CPU and GPU a couple of TM1637 displays and an RPI pico.
If you already own a soldering iron and some scraps of cable you cand build this thing for around 15 bucks.

## Pico Wiring Diagram

### Parts List

  * HW069 / TM1637 4 digit 7 part display
  * Rasberry Pi Pico

### Wiring Diagram

![Wiring Diagram for the RPI Pico](media/schematic.svg "Wiring Diagram for the RPI Pico")

## Installation 

  * Download the UF2 file for the Pico from here: https://www.raspberrypi.com/documentation/microcontrollers/micropython.html
  * Download and install Thonny: https://thonny.org/
  * Plug your RPI Pico into your PC while holding down the BOOTSEL button.
  * Copy the UF2 image into your PICO device in using file explorer (it should be called RPI-RP2).
      - After doing this the device will restart.
  * Start Thonny.
     - Copy and paste script in PicoDisplayApp\main.py into the code editor.
     - Press the save button, a prompt should pop up asking you where you want to save the file. Save it to the Pico using the name `main.py`
  * Copy the project Temp Sensor App directory somewhere on to your PC.
  * Run the TempSensorApp.exe file.
    - If you've setup everything successfully, the you should see the temperatures pop up on each display.

