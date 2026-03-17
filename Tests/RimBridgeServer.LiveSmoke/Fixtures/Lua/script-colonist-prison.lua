rb.assert(params.screenshotFileName ~= nil, "params.screenshotFileName is required.")

local screenshotFileName = params.screenshotFileName

local categories = rb.call("rimworld/list_architect_categories", {
  includeHidden = false,
  includeEmpty = false
})

local structureCategoryId = nil
for _, category in ipairs(categories.result.categories) do
  if structureCategoryId == nil and category.id == "architect-category:structure" then
    structureCategoryId = category.id
  end
end
rb.assert(structureCategoryId ~= nil, "Expected the Structure architect category.")

local designators = rb.call("rimworld/list_architect_designators", {
  categoryId = structureCategoryId,
  includeHidden = false
})

local wallDesignatorId = nil
for _, designator in ipairs(designators.result.designators) do
  if wallDesignatorId == nil and designator.buildableDefName == "Wall" then
    wallDesignatorId = designator.id
  end
end
rb.assert(wallDesignatorId ~= nil, "Expected a Wall build designator in the Structure architect category.")

local colonists = rb.call("rimworld/list_colonists", { currentMapOnly = true })
rb.assert(colonists.result.count >= 3, "Expected at least three current-map colonists.")

local selectedColonists = {
  colonists.result.colonists[1],
  colonists.result.colonists[2],
  colonists.result.colonists[3]
}

local searchOriginX = selectedColonists[1].position.x
local searchOriginZ = selectedColonists[1].position.z
local rallyX = nil
local rallyZ = nil
local planningAttempts = 0
local searchRadius = 4

while rallyX == nil and planningAttempts < 12 do
  planningAttempts = planningAttempts + 1

  local candidate = rb.call("rimworld/find_random_cell_near", {
    x = searchOriginX,
    z = searchOriginZ,
    startingSearchRadius = searchRadius,
    maxSearchRadius = searchRadius + 8,
    width = 3,
    height = 3,
    footprintAnchor = "center",
    requireWalkable = true,
    requireStandable = true,
    requireNoImpassableThings = true
  })

  if candidate.result.success == true then
    local candidateX = candidate.result.cell.x
    local candidateZ = candidate.result.cell.z

    local northDryRun = rb.call("rimworld/apply_architect_designator", {
      designatorId = wallDesignatorId,
      x = candidateX - 2,
      z = candidateZ - 2,
      width = 5,
      height = 1,
      dryRun = true,
      keepSelected = false
    })
    local southDryRun = rb.call("rimworld/apply_architect_designator", {
      designatorId = wallDesignatorId,
      x = candidateX - 2,
      z = candidateZ + 2,
      width = 5,
      height = 1,
      dryRun = true,
      keepSelected = false
    })
    local westDryRun = rb.call("rimworld/apply_architect_designator", {
      designatorId = wallDesignatorId,
      x = candidateX - 2,
      z = candidateZ - 1,
      width = 1,
      height = 3,
      dryRun = true,
      keepSelected = false
    })
    local eastDryRun = rb.call("rimworld/apply_architect_designator", {
      designatorId = wallDesignatorId,
      x = candidateX + 2,
      z = candidateZ - 1,
      width = 1,
      height = 3,
      dryRun = true,
      keepSelected = false
    })

    local wallsAccepted =
      northDryRun.result.acceptedCellCount == 5 and
      southDryRun.result.acceptedCellCount == 5 and
      westDryRun.result.acceptedCellCount == 3 and
      eastDryRun.result.acceptedCellCount == 3

    if wallsAccepted then
      rallyX = candidateX
      rallyZ = candidateZ
    end
  end

  searchRadius = searchRadius + 2
end

rb.assert(rallyX ~= nil and rallyZ ~= nil, "Expected to find an accepted rally cell for the prison layout.")

rb.call("rimworld/clear_selection")

for i, colonist in ipairs(selectedColonists) do
  rb.call("rimworld/select_pawn", { pawnName = colonist.name, append = i > 1 })
  rb.call("rimworld/set_draft", { pawnName = colonist.name, drafted = true })
end

local interiorMinX = rallyX - 1
local interiorMaxX = rallyX + 1
local interiorMinZ = rallyZ - 1
local interiorMaxZ = rallyZ + 1

rb.print("planning_attempts", planningAttempts)
rb.print("rally", { x = rallyX, z = rallyZ })

rb.call("rimworld/right_click_cell", { x = rallyX, z = rallyZ })
rb.call("rimworld/pause_game", { pause = false })

local grouped = rb.poll("rimworld/list_colonists", { currentMapOnly = true }, {
  timeoutMs = 10000,
  pollIntervalMs = 100,
  condition = {
    all = {
      { path = "result.colonists", countEquals = 3 },
      {
        path = "result.colonists",
        allItems = {
          path = "position.x",
          greaterThanOrEqual = interiorMinX,
          lessThanOrEqual = interiorMaxX
        }
      },
      {
        path = "result.colonists",
        allItems = {
          path = "position.z",
          greaterThanOrEqual = interiorMinZ,
          lessThanOrEqual = interiorMaxZ
        }
      },
      {
        path = "result.colonists",
        allItems = {
          path = "job",
          ["in"] = { "Wait_Combat", "Wait_MaintainPosture" }
        }
      }
    }
  }
})

rb.print("grouped_attempts", grouped.attempts)

rb.call("rimworld/pause_game", { pause = true })
rb.call("rimworld/set_god_mode", { enabled = true })

local wallSegments = {
  { x = rallyX - 2, z = rallyZ - 2, width = 5, height = 1 },
  { x = rallyX - 2, z = rallyZ + 2, width = 5, height = 1 },
  { x = rallyX - 2, z = rallyZ - 1, width = 1, height = 3 },
  { x = rallyX + 2, z = rallyZ - 1, width = 1, height = 3 }
}

for _, wallSegment in ipairs(wallSegments) do
  rb.call("rimworld/apply_architect_designator", {
    designatorId = wallDesignatorId,
    x = wallSegment.x,
    z = wallSegment.z,
    width = wallSegment.width,
    height = wallSegment.height,
    dryRun = false,
    keepSelected = true
  })
end

for _, colonist in ipairs(selectedColonists) do
  rb.call("rimworld/set_draft", { pawnName = colonist.name, drafted = false })
end

rb.call("rimworld/pause_game", { pause = false })

local captured = rb.poll("rimworld/list_colonists", { currentMapOnly = true }, {
  timeoutMs = 5000,
  pollIntervalMs = 100,
  condition = {
    all = {
      {
        path = "result.colonists",
        allItems = {
          path = "drafted",
          equals = false
        }
      },
      {
        path = "result.colonists",
        allItems = {
          path = "position.x",
          greaterThanOrEqual = interiorMinX,
          lessThanOrEqual = interiorMaxX
        }
      },
      {
        path = "result.colonists",
        allItems = {
          path = "position.z",
          greaterThanOrEqual = interiorMinZ,
          lessThanOrEqual = interiorMaxZ
        }
      }
    }
  }
})

local capture = rb.call("rimworld/take_screenshot", {
  fileName = screenshotFileName,
  includeTargets = false
})

return {
  wallDesignatorId = wallDesignatorId,
  rallyX = rallyX,
  rallyZ = rallyZ,
  interiorMinX = interiorMinX,
  interiorMaxX = interiorMaxX,
  interiorMinZ = interiorMinZ,
  interiorMaxZ = interiorMaxZ,
  planningAttempts = planningAttempts,
  groupedAttempts = grouped.attempts,
  capturedAttempts = captured.attempts,
  screenshotPath = capture.result.path
}
