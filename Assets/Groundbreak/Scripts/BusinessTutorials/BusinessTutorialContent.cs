namespace Groundbreak
{
    /// <summary>The text of the business tutorials. Numbers come from the game code (RepairJobs, VanCargo, Yard.Levels,
    /// FleetCatalog, Finance, Crew, Builder, Developments, Company.RecomputeStage) - update here if those change.
    /// Pictures: bt_*.png, captured by BusinessTutorialTests.B_CaptureBusinessCareer.</summary>
    public static partial class BusinessTutorials
    {
        static void Handyman()
        {
            New("biz-handyman", "Your first service call", "Start with a van and $2,500: take a repair call, find the problem, fix it, get paid.")
                .Step("Your van and your starting kit",
                    "A new Career game starts in your driveway at 108 Main St with $2,500, a used work van, basic hand tools and common parts already on the van's shelves (P-traps, supply lines, outlets, a GFCI, patch, caulk, paint). Small repairs are how most contractors start.",
                    "T = tablet  ·  walk to the van door and press E to drive", "bt_01_van_home", find: VanSpot, where: "your van")
                .Step("Pick a service call",
                    "Open the tablet (T), then the Jobs app, tab 'Service calls'. Each call shows the address, what the customer noticed and any tools you may need. You charge $85 an hour labor, a $65 trip charge and parts at cost + 20%. Press Accept. Until you hire a crew you can run one job at a time.",
                    "Tablet → Jobs → Service calls → Accept", "bt_02_jobs_service_calls", openApp: "Jobs")
                .Step("Drive to the house",
                    "On your active job press 'Navigate' for an arrow to the address, or 'Drive there' to skip the trip (the clock moves ahead by the drive time). Park in the driveway: parts on the van can be used while it is parked within 130 ft.",
                    "Jobs → Navigate or Drive there  ·  W/S/A/D drive, Space brake", "bt_03_parked_at_call", openApp: "Jobs")
                .Step("Find the real problem first",
                    "Go to what the customer described and look before you fix. A leak under a sink can be a loose slip nut or a cracked trap, and each needs a different fix. Run the test action (for a leak: wipe every joint dry, run the faucet, watch for the first drip). The answer shows as 'Last test'.",
                    "Aim at the problem (within about 8 ft)  ·  Q / Z / mouse wheel = choose an action", "bt_04_diagnose_leak")
                .Step("Do the repair the right way",
                    "The repair panel lists each action and the tool it needs: green = in your hand, orange = you own it but it's not in your hand, red = you don't own it. Hold the left mouse button to do the action. Do the prep too (bucket under the trap, water off). Skipping it, or doing the wrong repair, lowers quality and makes a mess.",
                    "Hold left mouse = work  ·  hold the tool shown in green", "bt_05_repair_panel")
                .Step("Test it and clean up",
                    "When the fix is done, test again: no drip means fixed. Then dry and tidy up. The customer scores you on quality (45%), how much got fixed (35%), being on time (10%) and leaving it clean (10%).",
                    "Run the test action again after the fix", "bt_06_fixed_leak")
                .Step("Send the invoice",
                    "Press B on site, or 'Send invoice' in the Jobs app. A happy customer pays the full bill, and at 90%+ they tip 10%. An unhappy one pays less, and every late day costs 2%. If something isn't fixed or the work is poor, they call you back in 1-3 days for a free return visit. Skip it and you lose reputation.",
                    "B = invoice (press twice to bill an unfinished job)  ·  Jobs → Send invoice", "bt_07_invoice", openApp: "Jobs");
        }

        static void Van()
        {
            New("biz-van", "The van and fast travel", "What fits in the van and on the roof rack, driving, and fast travel.")
                .Step("What fits in the van",
                    "The van holds about 180 cubic feet behind the seats (the shelves take the rest) and 3,200 lb. Inside: pieces up to 10 ft long and up to 20 sheets laid flat. The roof rack carries up to 6 long pieces (16 ft max, 150 lb) plus your 24 ft ladder.",
                    "E at the rear doors = open/close  ·  E at the roof rack = take/stow the ladder", "bt_10_van_cargo", find: VanSpot, where: "your van")
                .Step("The Van app",
                    "The Van app shows how full the van is (space and weight bars) and what's on the roof rack. Parked at a build site you can unload and load material here: - unloads, + loads, hold Shift for 10 at a time.",
                    "Tablet → Van", "bt_11_van_app", openApp: "Van")
                .Step("Driving",
                    "Walk to the driver's door and press E. W = gas, S = brake / reverse, A and D = steer, Space = brake. V switches to a camera behind the van. Stop before you get out (E again).",
                    "E = get in/out  ·  W/S/A/D  ·  Space = brake  ·  V = chase camera", "bt_12_driving")
                .Step("Fast travel",
                    "In the Van app, 'Drive to' takes you straight to Home, the Hardware store, the Build site or your HQ. It costs no money, but the clock moves ahead by the time the drive takes at 25 mph, so plan your day.",
                    "Tablet → Van → Drive to", "bt_13_fast_travel", openApp: "Van");
        }

        static void Hardware()
        {
            New("biz-hardware", "Hardware store parts", "Hartley's Hardware at 201 Main St: repair parts and some lumber, loaded straight into your van.")
                .Step("Drive to the store",
                    "Hartley's Hardware & Lumber is at 201 Main St. Drive there (or Van app → Drive to → Hardware store) and park in its lot. The store only serves you when your van is parked close by.",
                    "Van app → Drive to → Hardware store", "bt_20_store_front", find: StoreSpot, where: "the hardware store")
                .Step("Buy what the job needs",
                    "Open the Hardware Store app. Each line shows the price, the pack size and how many you already have in the van. Press 'Buy 1' or 'Buy 5' packs. Before a call, check which parts it may need: a second trip costs time.",
                    "Tablet → Hardware Store → Buy 1 / Buy 5", "bt_21_store_app", openApp: "Hardware Store")
                .Step("It goes straight in the van",
                    "There is no delivery: what you buy is loaded into the van. If the van is out of space or weight, the store won't sell you more. For big lumber orders later, use the Warehouse app's material runs or have the supplier deliver.",
                    "Van app = how full you are", "bt_22_loaded_at_store", openApp: "Van");
        }

        static void Warehouse()
        {
            New("biz-warehouse", "Warehouse and a truck", "Become a Contractor: lease a bay, buy a truck, unlock bigger jobs.")
                .Step("Lease a warehouse",
                    "Open the Warehouse app. A Rented bay costs $4,500 to move in, then about $82 a day (rent $70 + insurance $12, about $2,500 a month) plus utilities. It stores 1,800 cu ft of material, parks 3 vehicles and gets you 8% off bulk material. Bigger levels come later: Yard + shop ($15,000 move-in) and Headquarters ($125,000).",
                    "Tablet → Warehouse → Lease", "bt_30_warehouse_app", openApp: "Warehouse")
                .Step("Your bay",
                    "The warehouse sits on its own lot in town, and your van moves into a stall there. 'Go to the warehouse' takes you there. Store material at a discount and haul it to jobs with a Material run: pick it up, haul it to the warehouse, or load and drive to the site.",
                    "Warehouse → Go to the warehouse", "bt_31_warehouse_bay", find: YardSpot, where: "your warehouse")
                .Step("Buy a truck at the Dealership",
                    "Each kind of job needs a certain truck: fences and decks a pickup, sheds a flatbed, driveways and re-roofs a dump truck, garages a boom truck. A used half-ton pickup is $17,000 cash. Short on cash? Finance it: 10% down new (20% used), then a daily payment over 36, 60 or 84 days.",
                    "Tablet → Dealership → 'Used $ cash' or a finance button", "bt_32_dealership", openApp: "Dealership")
                .Step("You're a Contractor",
                    "With a warehouse and a truck (not counting the van), the company becomes a Contractor and the Jobs app starts showing decks, fences, sheds, garages and concrete work. Trucks cost insurance, fuel and maintenance every day: see the Company app.",
                    "Tablet → Company = your stage and what's next", "bt_33_inside_bay", openApp: "Company");
        }

        static void Contractor()
        {
            New("biz-contractor", "Taking a contractor job", "Decks, fences, sheds, garages, driveways and remodels: accept, build, pass the walk-through, get paid.")
                .Step("Choose a job",
                    "Open the Jobs app, tab 'Contractor'. Each offer shows the size, the pay, the due day, the rough material cost and the truck it needs. 'Scope' shows exactly what the client wants. Fences pay about $40 a foot, decks about $42-52 a sq ft. Remodels (bathrooms, kitchens, windows) are billed by the hour from the Service calls tab.",
                    "Tablet → Jobs → Contractor → Scope / Accept", "bt_40_jobs_contractor", openApp: "Jobs")
                .Step("Find the lot and order material",
                    "Drive to the address and look for the orange-flagged stakes and the job sign. The active job has a material list showing 'in stock' or 'have X of Y'. Press 'Order what's missing'. Order concrete from the Concrete Dispatch app.",
                    "Jobs → active job → Order what's missing", "bt_41_empty_lot", openApp: "Jobs")
                .Step("Build it to code",
                    "Build with the same tools as the rest of the game: posts set in concrete, rails, pickets, footings below the frost line. The client checks real code items: deck ledger flashing, deep enough footings, guards at least 36 in. tall, baluster gaps under 4 in. Each miss cuts your quality by 15%.",
                    "Use the trade tools (Tab = tool list)", "bt_42_fence_going_up")
                .Step("Client walk-through and final payment",
                    "The job panel shows a live 'Client walk-through' checklist and your workmanship %. When everything is ticked, press 'Request final payment'. Late jobs lose 2% of the pay for each day late, so watch the due day.",
                    "Jobs → active job → Request final payment", "bt_43_fence_done", openApp: "Jobs");
        }

        static void CrewTut()
        {
            New("biz-crew", "Hiring and running a crew", "Hire, assign, give tasks, pay them and keep them happy.")
                .Step("Hire workers",
                    "Open the Crew app, tab 'Hire'. Seven people look for work each morning (the first two are always a laborer and a carpenter). Each shows a trade, a skill level (Helper up to Master) and a daily wage: a laborer gets about $136-208 a day, a carpenter $160-320. Hire 2 and the company becomes a Crew, so you can run several jobs at once.",
                    "Tablet → Crew → Hire", "bt_50_crew_hire", openApp: "Crew")
                .Step("Assign them to a job",
                    "On the 'Crew' tab, 'Assign: next job' sends a worker to your build site, an active job, a spec house or back to the yard. Workers on a job away from you keep building it day by day. More workers and higher skill = faster.",
                    "Crew → Assign: next job", "bt_51_crew_assign", openApp: "Crew")
                .Step("Give tasks on site",
                    "On the job, press K at a spot to give a task: fasten loose pieces, build this, drywall or paint a room, dig a trench or footing, lay sod, carry and stock material. The 'Tasks' tab lists them. Use 'Redo' or X to change them.",
                    "K = task menu on site  ·  Crew → Tasks", "bt_52_crew_tasks", openApp: "Crew")
                .Step("Pay them and keep morale up",
                    "Workers arrive in the crew truck at the start of the day and work with real materials. Payroll comes out every morning. Morale goes up with fair pay, raises and training. It goes down with overtime, rain and sitting idle at the yard. Below 12% morale, a worker quits.",
                    "Crew → Raise 5%  ·  Train  ·  Overtime on/off", "bt_53_crew_at_job", openApp: "Crew");
        }

        static void Finance()
        {
            New("biz-finance", "Money, costs and loans", "Know your cash and what each day costs, and borrow safely.")
                .Step("Your company at a glance",
                    "The Company app shows cash, debt, reputation, jobs done and equity, plus a checklist of what you need for the next stage.",
                    "Tablet → Company", "bt_60_company", openApp: "Company")
                .Step("Daily costs",
                    "Every morning the bills are paid automatically: warehouse rent, insurance, utilities, vehicle insurance and maintenance, fuel, crew payroll and loan payments. Your jobs must bring in more than this every day.",
                    "Company → Daily costs (charged each morning)", "bt_61_daily_costs", openApp: "Company")
                .Step("Profit and loss",
                    "Profit & loss splits what came in (invoices, draws, sales) from what went out (material, wages, vehicles, rent). Switch between Today, Last 7 days and All time to see whether a week actually made money.",
                    "Company → Profit & loss → Today / Last 7 days / All time", "bt_63_profit_loss", openApp: "Company")
                .Step("Borrowing, and not going broke",
                    "Need cash for a truck or a big job's material? Set an amount, pick 30, 60 or 120 days and press Borrow. Bank loans are 9.5% a year, and your limit grows with your stage ($10k as a Handyman up to $1.5M as a Developer). If you can't pay the bills, you get a $35 late fee. Three unpaid days in a row = bankruptcy: your assets are sold, you lose 15 reputation and drop back a stage.",
                    "Company → Borrow  ·  Pay off", "bt_62_loans", openApp: "Company");
        }

        static void Plans()
        {
            New("biz-plans", "Reading a blueprint", "Pick a plan, read the rooms and sizes, and see the material list and cost.")
                .Step("Choose a plan",
                    "The Plans app holds every plan in the game, filtered by type: Residential, Outbuilding, Commercial, Industrial. Pick one to see its floor plan drawn to scale, with the overall size and the number of floors.",
                    "Tablet → Plans → filter → pick a plan", "bt_70_plans", openApp: "Plans")
                .Step("Rooms, walls and openings",
                    "Under the drawing: each room with its size in feet and inches, the total wall length, the doors and windows, and the inspections the building needs. 'Show overlay on my site' marks the plan out on the ground so you can build on the lines. 'Floor N of M' switches floors.",
                    "Plans → Show overlay on my site  ·  Floor N of M", "bt_71_plan_rooms", openApp: "Plans")
                .Step("Material takeoff and cost",
                    "The 'Material takeoff' is counted from the real dimensions (studs, sheets, concrete, roofing...) with the cost per square foot and the crew days it takes. Use it to price a job before you sign.",
                    "Plans → Material takeoff", "bt_72_takeoff", openApp: "Plans");
        }

        static void BuilderTut()
        {
            New("biz-builder", "A full house for a client", "Builder stage: sign a house contract, run the build with subs and your crew, get paid in draws.")
                .Step("Sign a house contract",
                    "At the Builder stage (reputation 65 and a Yard + shop), the Jobs app's 'Houses' tab lists client houses: a 1,200 sq ft starter home ($185,000), a two-story ($395,000, reputation 75) and a custom home ($660,000, reputation 85). Press Sign. The client pays a 10% deposit.",
                    "Tablet → Jobs → Houses → Sign", "bt_80_jobs_houses", openApp: "Jobs")
                .Step("Foundation first",
                    "The house goes on a town lot, phase by phase in real order: foundation, framing, rough-in, drywall, finish. Subcontractors keep the plan's schedule on their own. You can also choose 'Work this house myself' and build it with your own hands.",
                    "Jobs → Houses → your house", "bt_81_house_foundation", openApp: "Jobs")
                .Step("Speed it up with your crew",
                    "Every worker you assign speeds the house up (4 journeymen about double the pace). Each passed inspection pays a draw: foundation 10%, framing 20%, rough-in 20%, drywall 15%.",
                    "Crew → Assign: next job → the house", "bt_82_house_going_up", openApp: "Crew")
                .Step("Handover, change orders and warranty",
                    "After framing the client may ask for change orders (a porch, recessed lights, quartz tops). Accept them for extra pay. The last 25% is paid at handover. About 30 days later a warranty call comes in: send a carpenter ($220) within 14 days or your reputation takes a hit.",
                    "Jobs → Houses → change order Accept  ·  Warranty call: send a carpenter", "bt_83_house_handover", openApp: "Jobs");
        }

        static void Developer()
        {
            New("biz-developer", "Developing a neighborhood", "Developer stage: buy raw land, split it into lots, put in a street, build spec houses and sell them.")
                .Step("Buy land",
                    "At the Developer stage (reputation 80, Headquarters and 8 workers), the Jobs app's 'Land' tab lists raw land at about $32,000-44,000 an acre plus 2% closing costs. Buy a tract you can afford to build on.",
                    "Tablet → Jobs → Land → Buy", "bt_90_jobs_land", openApp: "Jobs")
                .Step("Look at the raw land",
                    "Your tract starts as empty ground along a street. Wider lots hold bigger houses but you get fewer of them.",
                    "Development app = your tracts", "bt_91_raw_land", openApp: "Development")
                .Step("Split it into lots and put in the street",
                    "In the Development app press 'Plat 60' / 70' / 80' lots' ($8,500 fee + $1,200 per lot), then 'Hire site work' for the street, curbs, water, sewer and power ($630 per foot of street + $3,500 per lot). Nothing can be built until the site work is done.",
                    "Development → Plat lots → Hire site work", "bt_92_development_app", openApp: "Development")
                .Step("Build spec houses",
                    "Pick a spec plan and a finish (Builder grade, Standard or Premium) and press Build. Permits and impact fees are paid up front, and a construction loan covers 80% of the build at 8.5%. Assign your crew to the houses to speed them up.",
                    "Development → Spec plan → Build  ·  Crew → Assign", "bt_93_spec_houses", openApp: "Development")
                .Step("List and sell",
                    "When a house is finished, 'List at value' sells in about two weeks. 'List +5%' earns more but takes longer, and a price more than 15% over value never sells. The agent takes 5.5%, plus 1.5% closing. Value depends on size, finish, the neighborhood, your workmanship and the market.",
                    "Development → List at value / List +5%", "bt_94_listing", openApp: "Development")
                .Step("Sold out",
                    "When the last house sells, the neighborhood is complete and the profit is yours. Buy the next tract and do it again, bigger.",
                    "Development → profit per neighborhood", "bt_95_sold_out", openApp: "Development");
        }

        static void Reputation()
        {
            New("biz-reputation", "Reputation and stages", "How reputation works and what each stage needs.")
                .Step("Reputation",
                    "You start at 50. Each finished job moves it from -3 to +3 by quality, and -1 more if it's late. A failed callback costs 2, an ignored one 4, an unpaid bill 1 and bankruptcy 15. Good work on time is the only way up, and bigger jobs need more of it.",
                    "Company app = your reputation", "bt_A0_headquarters", openApp: "Company")
                .Step("The five stages",
                    "Handyman: a van and service calls. Contractor: lease a warehouse and own a truck. Crew: hire 2 workers. Builder: reputation 65 and a Yard + shop. Developer: reputation 80, Headquarters and 8 workers. Your stage never goes down unless you go bankrupt. The Company app's checklist shows what's missing.",
                    "Company → Next stage checklist", "bt_A1_stage_checklist", openApp: "Company");
        }
    }
}
