# Usage Measurement Service for AutoCAD
A web service that makes it possible for admins to centrally measure the usage times of AutoCAD installations in a corporate network.

## Motivation
After the switch of Autodesk products to named user licensing, it is possible that the usage statistics offered by Autodesk are no longer sufficient for the needs of some organizations. The new usage statistics are only available in the temporal resolution of a whole day. This means that it is only measured whether the Autodesk product was opened once on a certain day.

However, in some cases measurements with a higher temporal resolution are required. For example, this is the case when the departments of an organization want to share the license costs fairly among themselves based on the actual usage. Therefore a new solution is needed.

## How to operate
This web service works in tandem with a plug-in that is also provided as open-source in another repository, which is:

https://github.com/Fachstelle-Geoinformation-Winterthur/acad_usage_measurement_client

You need to install and run the plug-in, in order to use this web service.
