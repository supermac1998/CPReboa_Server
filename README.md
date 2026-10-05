# CPReboa Server

Central data-acquisition and processing software developed for the **CPReboa study**. The system coordinates acquisition of physiologic and audio data, communicates with the CPReboa tablet application, stores prospectively recorded events, and generates study-specific output files.

## Components

### CPReboaMonitorLauncher
Central server application coordinating the data-acquisition workflow. It communicates with the tablet application, manages study cases, starts and stops the individual acquisition programs, monitors their status, and stores recorded clinical and procedural events.

### VSCaptureMP
Adapted acquisition software for the **Philips IntelliVue MP30** patient monitor. It acquires and stores physiologic data, including arterial pressure and end-tidal CO₂ measurements.

### Masimo_Root
Acquisition software for the **Masimo Root with O3 regional oximetry**, used to record bilateral cerebral regional oxygen saturation (rSO₂) data.

### MicroRecording
Audio acquisition software for recording audio during resuscitation. Recordings are stored together with timing metadata for subsequent temporal alignment with the other acquired data.

### CPReboaReporting
Post-processing and reporting component used to generate study-specific output from the acquired data, including a CSV dataset for transfer to REDCap and a PDF study report for documentation and data verification.

## Data Acquisition

The individual acquisition programs are coordinated by `CPReboaMonitorLauncher`. Physiologic measurements and recorded events use the central server clock as a common UTC-based time reference, allowing the independently acquired data to be temporally aligned.

Continuous recordings are retained in case-specific directories. For predefined CPReboa study time points, corresponding physiologic measurements can be extracted from the continuous recordings for study-specific data processing and reporting.

## System Integration

The server communicates with the **CPReboa Event-Logging Application** running on an Android tablet. The application provides acquisition control, status monitoring, prospective event logging, and entry of additional study variables.

## Research Context

This software was developed for the **CPReboa study**, investigating in-hospital cardiopulmonary resuscitation with resuscitative endovascular balloon occlusion of the aorta (REBOA) in patients with non-traumatic cardiac arrest.

## Disclaimer

This software was developed for research purposes within the CPReboa study. It is not a certified medical device and is not intended for independent clinical use.

## Author

Alexander Macpherson  
Universität Bern

## License

[License information]
