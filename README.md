PartsPortal (PP)
PartsPortal (PP) is a Windows desktop app used to build and send material request PDFs (including backorders) and generate Outlook emails with the PDFs attached. It supports multiple site tabs, warehouse-based parts lists, and configurable email recipients.
Quick Start (Tester Instructions)
1.	Unzip the provided package to a normal folder (recommended):
	o	C:\Apps\PartsPortal\
		Avoid protected locations like C:\Program Files\ and sometimes Downloads (permissions can be weird).
2.	Run:
	o	PartsPortal.exe
3.	In the app:
	o	Select a Warehouse
	o	Fill in site details / add parts
	o	Generate the PDF
	o	Click Email to open an Outlook draft (or send, depending on settings)
________________________________________
Requirements
•	Windows 11
•	Microsoft Outlook (desktop app RUNNING IN CLASSIC MODE) installed (PartsPortal uses Outlook to create drafts/send emails).
	To check for .NET 8 Desktop Runtime on a laptop:
		dotnet --list-runtimes
	Look for:
		•	Microsoft.WindowsDesktop.App 8.0.x
	If it’s missing, use the PartsPortalx64 package.
________________________________________
Settings
PartsPortal includes a Settings window where you can configure:
•	Name / Truck #
•	PDF output folder
•	Parts CSV path
•	Email To / CC
•	Email directory entries (with search)
Tip: If the parts list doesn’t show, double-check the Parts CSV Path in Settings.
________________________________________
Email Behavior
•	Emails are created through Outlook automation.
•	Required CC is automatically included:
o	smartgridradio@centerpointenergy.com (if not already present)
•	Backorders emails:
________________________________________
Files & Data (Where Stuff Saves)
Depending on configuration, PartsPortal typically stores:
•	Generated PDFs in your chosen PDF Output Folder
•	User settings and directories either:
	o	in a per-user AppData folder, or
	o	alongside the app (if running from an unzipped folder)
		If you need to locate settings quickly, search your user profile for:
			•	settings.json
________________________________________
Troubleshooting
“Could not copy apphost.exe… file is locked”
PartsPortal is still running (sometimes twice).
•	Open Task Manager
•	End all PartsPortal.exe processes
•	Rebuild / republish
App won’t start on coworker laptop
If using the Framework-Dependent package:
•	The laptop may not have .NET 8 Desktop Runtime
•	Use the Self-Contained package instead
Email draft doesn’t appear / Outlook hangs
•	Confirm Outlook is installed and you can open it normally
•	Try closing Outlook completely and reopening it
•	Re-test with “Open Draft” mode enabled in Settings
Parts list is empty
•	Confirm the Parts CSV Path points to the correct shared CSV
•	If needed, reselect it using the Browse button in Settings
________________________________________
Versioning
PartsPortal version is Not Released until testing is completed. Full release date tbd. 
________________________________________
Support / Feedback
For testing feedback, report:
•	What you clicked
•	What you expected
•	What happened instead
•	Any error message text
•	Whether you used Self-contained or Framework-dependent
