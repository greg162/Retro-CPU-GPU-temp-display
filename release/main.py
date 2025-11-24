from machine import Pin
import time
import sys
import select

class TM1637:
    def __init__(self, clk, dio):
        self.clk = Pin(clk, Pin.OUT)
        self.dio = Pin(dio, Pin.OUT)
        self.brightness = 7
        
    def _start(self):
        self.dio.value(0)
        time.sleep_us(2)
        
    def _stop(self):
        self.dio.value(0)
        time.sleep_us(2)
        self.clk.value(1)
        time.sleep_us(2)
        self.dio.value(1)
        
    def _write_byte(self, b):
        for i in range(8):
            self.clk.value(0)
            time.sleep_us(2)
            self.dio.value((b >> i) & 1)
            time.sleep_us(2)
            self.clk.value(1)
            time.sleep_us(2)
        self.clk.value(0)
        time.sleep_us(2)
        self.clk.value(1)
        time.sleep_us(2)
        self.clk.value(0)
        time.sleep_us(2)
        
    def write(self, segments):
        self._start()
        self._write_byte(0x40)
        self._stop()
        self._start()
        self._write_byte(0xC0)
        for seg in segments:
            self._write_byte(seg)
        self._stop()
        self._start()
        self._write_byte(0x88 | self.brightness)
        self._stop()

# Digit to 7-segment mapping
DIGITS = [
    0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F
]
DEGREE = 0x63  # degree symbol
C = 0x39       # letter C
DASH = 0x40    # minus sign
E = 0x79       # letter E for Error

# Initialize displays
cpu_display = TM1637(clk=0, dio=1)  # Display 1 for CPU
gpu_display = TM1637(clk=2, dio=3)  # Display 2 for GPU

def show_status(code):
    """Show status code on both displays
    1 = Boot successful
    2 = Waiting for connection
    3 = Connected, waiting for data
    E-XX = Error codes
    """
    if code == 1:
        # Show "1   " on both
        cpu_display.write([DIGITS[1], 0, 0, 0])
        gpu_display.write([DIGITS[1], 0, 0, 0])
    elif code == 2:
        # Show "2   " on both
        cpu_display.write([DIGITS[2], 0, 0, 0])
        gpu_display.write([DIGITS[2], 0, 0, 0])
    elif code == 3:
        # Show "3   " on both
        cpu_display.write([DIGITS[3], 0, 0, 0])
        gpu_display.write([DIGITS[3], 0, 0, 0])

def show_error(error_code):
    """Show error code E-XX on both displays"""
    tens = error_code // 10
    ones = error_code % 10
    cpu_display.write([E, DASH, DIGITS[tens], DIGITS[ones]])
    gpu_display.write([E, DASH, DIGITS[tens], DIGITS[ones]])

def display_temp(display, temp):
    """Display temperature on a single display (e.g., 72°C)"""

# If C# sends 0.0, we show dashes instead of "0C"
    if temp == 0:
        display.write([0, DASH, DASH, 0]) # Shows " -- "
        return

    # Handle out of range temps
    if temp < 0 or temp > 999:
        display.write([E, DASH, DASH, DASH])
        return
    
    # Format: XX°C
    temp_int = int(round(temp))

    # Handle 100+ degrees (Remove 'C' to fit 3 digits)
    if temp >= 100:
        if temp_int > 999: temp_int = 999 # Cap at 999
        
        hundreds = temp_int // 100
        tens = (temp_int % 100) // 10
        ones = temp_int % 10
        
        # Shows "105 " (No C)
        display.write([DIGITS[hundreds], DIGITS[tens], DIGITS[ones], 0]) 
        return
    else:
        # Get tens and ones digits
        tens = temp_int // 10
        ones = temp_int % 10
        
        # Build segments
        segments = [
            0, # Blank leading digit
            DIGITS[tens] if tens > 0 else 0,  # Blank leading zero
            DIGITS[ones],
            C
        ]
        display.write(segments)
        return

# Status 1: Boot successful
print("Status 1: Boot successful")
show_status(1)
time.sleep(2)

# Status 2: Waiting for connection
print("Status 2: Waiting for serial connection")
show_status(2)
time.sleep(1)

# Status 3: Connected, waiting for data
print("Status 3: Connected, waiting for temperature data")
show_status(3)

last_data_time = time.time()
error_shown = False

while True:
    try:
        # Check if data is available
        if select.select([sys.stdin], [], [], 0)[0]:
            line = sys.stdin.readline().strip()
            
            if line:
                print(f"Received: {line}")
                
                if ',' in line:
                    parts = line.split(',')
                    try:
                        cpu_temp = float(parts[0])
                        gpu_temp = float(parts[1])
                        
                        display_temp(cpu_display, cpu_temp)
                        display_temp(gpu_display, gpu_temp)
                        
                        last_data_time = time.time()
                        error_shown = False
                        
                        print(f"Displayed - CPU: {cpu_temp}°C, GPU: {gpu_temp}°C")
                    except ValueError:
                        print(f"Error parsing data: {line}")
                        show_error(10)  # E-10: Parse error
                        error_shown = True
                else:
                    print(f"Invalid format: {line}")
                    show_error(11)  # E-11: Format error
                    error_shown = True
        
        # Check for timeout (no data for 10 seconds)
        if not error_shown and (time.time() - last_data_time) > 10:
            print("Error: No data received for 10 seconds")
            show_error(20)  # E-20: Timeout
            error_shown = True
            
        time.sleep(0.1)
        
    except Exception as ex:
        print(f"Exception: {ex}")
        show_error(99)  # E-99: Unknown error
        time.sleep(1)