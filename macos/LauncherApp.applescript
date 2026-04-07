on run
	set appPath to POSIX path of (path to me)
	set projectDir to do shell script "dirname " & quoted form of appPath
	set launcherPath to projectDir & "/launcher.sh"
	
	tell application "System Events"
		if not (exists file launcherPath) then
			display dialog "launcher.sh non trovato in: " & projectDir buttons {"OK"} default button "OK" with icon caution
			return
		end if
	end tell
	
	tell application "Terminal"
		activate
		do script "cd " & quoted form of projectDir & " && chmod +x ./launcher.sh && ./launcher.sh"
	end tell
end run
