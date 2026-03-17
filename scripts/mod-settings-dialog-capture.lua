rb.assert(params.modId ~= nil, "params.modId is required.")
rb.assert(params.screenshotFileName ~= nil, "params.screenshotFileName is required.")

local modId = params.modId
local screenshotFileName = params.screenshotFileName
local targetIgnoreAssignments = params.ignoreAssignments
local targetIgnoreForbidden = params.ignoreForbidden
local targetIgnoreRestrictions = params.ignoreRestrictions
local targetMenuDelay = params.menuDelay
local targetWorkMarkers = params.workMarkers
local writeChanges = params.write == true

if targetIgnoreAssignments == nil then
  targetIgnoreAssignments = true
end

if targetIgnoreForbidden == nil then
  targetIgnoreForbidden = true
end

if targetIgnoreRestrictions == nil then
  targetIgnoreRestrictions = true
end

if targetMenuDelay == nil then
  targetMenuDelay = 500
end

if targetWorkMarkers == nil then
  targetWorkMarkers = "Static"
end

rb.call("rimbridge/wait_for_long_event_idle")

local opened = rb.call("rimworld/open_mod_settings", {
  modId = modId,
  replaceExisting = true
})

rb.assert(opened.result.success == true, "Opening the requested mod settings dialog failed.")

local updated = rb.call("rimworld/update_mod_settings", {
  modId = modId,
  write = writeChanges,
  values = {
    ignoreAssignments = targetIgnoreAssignments,
    ignoreForbidden = targetIgnoreForbidden,
    ignoreRestrictions = targetIgnoreRestrictions,
    menuDelay = targetMenuDelay,
    workMarkers = targetWorkMarkers
  }
})

rb.assert(updated.result.success == true, "Updating the requested mod settings failed.")

local settingsAfter = rb.call("rimworld/get_mod_settings", {
  modId = modId
})

rb.assert(settingsAfter.result.success == true, "Reading back the updated mod settings failed.")

local finalLayout = rb.call("rimworld/get_ui_layout", {
  timeoutMs = 3000
})

local finalSurface = nil
for _, surface in ipairs(finalLayout.result.surfaces) do
  if finalSurface == nil and surface.type == "RimWorld.Dialog_ModSettings" then
    finalSurface = surface
  end
end

rb.assert(finalSurface ~= nil, "The mod settings dialog was not available for screenshot capture.")

local capture = rb.call("rimworld/take_screenshot", {
  fileName = screenshotFileName,
  includeTargets = true,
  clipTargetId = finalSurface.captureTargetId,
  clipPadding = 12
})

rb.assert(capture.result.success == true, "Capturing the clipped mod settings screenshot failed.")

rb.print("capture_target_id", finalSurface.captureTargetId)
rb.print("screenshot_path", capture.result.path)

return {
  modId = modId,
  wroteSettings = writeChanges,
  requested = {
    ignoreAssignments = targetIgnoreAssignments,
    ignoreForbidden = targetIgnoreForbidden,
    ignoreRestrictions = targetIgnoreRestrictions,
    menuDelay = targetMenuDelay,
    workMarkers = targetWorkMarkers
  },
  screenshotPath = capture.result.path,
  screenshotSourcePath = capture.result.sourcePath,
  screenshotClipTargetId = capture.result.clipTargetId,
  settingsRoot = settingsAfter.result.root,
  dialogSurface = finalSurface,
  updates = updated.result.updates
}
