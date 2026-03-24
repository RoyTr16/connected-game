import requests
import time
import json
import os

# Add as many cities here as you want!
CITIES = ["Kfar Saba, Israel", "Ra'anana, Israel", "Hod Hasharon, Israel", "Petah Tikva, Israel", "Tel Aviv, Israel", "Ramat Gan, Israel", "Givatayim, Israel", "Bnei Brak, Israel", "Rishon LeZion, Israel", "Holon, Israel", "Bat Yam, Israel", "Haifa, Israel", "Jerusalem, Israel", "Ashdod, Israel", "Netanya, Israel", "Be'er Sheva, Israel", "Ashkelon, Israel", "Rehovot, Israel", "Kiryat Gat, Israel", "Eilat, Israel"]

OVERPASS_URL = "https://overpass-api.de/api/interpreter"
NOMINATIM_URL = "https://nominatim.openstreetmap.org/search"
MAX_RETRIES = 3 # How many times to try a city before giving up

def get_area_id(city_name):
    headers = {'User-Agent': 'DOTSTrafficSimulation/1.0'}
    # Ask for the top 5 results instead of 1
    params = {'q': city_name, 'format': 'json', 'limit': 5}

    response = requests.get(NOMINATIM_URL, params=params, headers=headers)
    response.raise_for_status()
    data = response.json()

    if not data:
        raise ValueError(f"Nominatim could not find a location matching: {city_name}")

    # Search through the results and prioritize the 'relation' (the city boundary)
    best_match = None
    for item in data:
        if item['osm_type'] == 'relation':
            best_match = item
            break

    # If no relation is found, fallback to the first result
    if best_match is None:
        best_match = data[0]

    osm_id = int(best_match['osm_id'])
    osm_type = best_match['osm_type']

    if osm_type == 'relation':
        return osm_id + 3600000000
    elif osm_type == 'way':
        return osm_id + 2400000000
    else:
        raise ValueError(f"Unsupported OSM type '{osm_type}' for area lookup.")

def build_query(area_id):
    return f"""
    [out:json][timeout:90];
    area({area_id})->.searchArea;
    (
      way["highway"~"motorway|trunk|primary|secondary|tertiary|unclassified|residential|living_street"](area.searchArea);
      way["highway"~"motorway_link|trunk_link|primary_link|secondary_link|tertiary_link"](area.searchArea);
      node["highway"~"traffic_signals|stop|give_way|crossing"](area.searchArea);
      node["traffic_sign"~"stop",i](area.searchArea);
      node["highway"="bus_stop"](area.searchArea);
      node["public_transport"~"platform|stop_position"](area.searchArea);
      relation["type"="restriction"](area.searchArea);
    );
    out body;
    >;
    out skel qt;
    """

for city in CITIES:
    # Format the filename safely
    safe_name = city.split(',')[0].replace(" ", "")
    filename = f"{safe_name}_Raw.json"

    # SKIP LOGIC: Don't waste quota on files we already have!
    if os.path.exists(filename):
        print(f"⏩ Skipping {city} - File '{filename}' already exists.")
        continue

    print(f"\nFetching data for {city}...")

    for attempt in range(MAX_RETRIES):
        try:
            area_id = get_area_id(city)
            print(f"  -> Found Area ID: {area_id}")

            query = build_query(area_id)
            response = requests.post(OVERPASS_URL, data={'data': query})

            # RETRY LOGIC: Handle rate limits specifically
            if response.status_code == 429:
                print(f"  ⚠️ Rate limited (429) on attempt {attempt + 1}/{MAX_RETRIES}.")
                print("  ⏳ Waiting 60 seconds for the server to cool down...")
                time.sleep(60)
                continue # Loop back and try this city again

            response.raise_for_status() # Catch any other errors like 500s

            with open(filename, 'w', encoding='utf-8') as f:
                json.dump(response.json(), f, ensure_ascii=False, indent=2)

            print(f"  ✅ Successfully saved {filename}")
            break # Success! Break out of the retry loop and go to the next city

        except Exception as e:
            print(f"  ❌ Attempt {attempt + 1} failed: {e}")
            if attempt < MAX_RETRIES - 1:
                print("  ⏳ Waiting 15 seconds before retrying...")
                time.sleep(15)
            else:
                print(f"  ⏭️ Giving up on {city} after {MAX_RETRIES} attempts.")

    # Respect public API rate limits between successful downloads
    print("Waiting 10 seconds before moving to the next city...")
    time.sleep(10)

print("\n🎉 All downloads complete!")