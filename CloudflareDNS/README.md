<h1 align="center">Jellyfin Plugin Cloudflare DNS</h1>

<p align="center">
Update your Cloudflare DNS A record with your dynamic IP address.
    
<a href="https://www.cloudflare.com/">Cloudflare.com</a>
</p>

## Configuration

1. **Hostname**: The hostname you want to update (e.g., `subdomain.example.com`)
2. **Cloudflare API Token**: Your Cloudflare API token with DNS edit permissions

To create a Cloudflare API token:
1. Go to [Cloudflare Dashboard](https://dash.cloudflare.com/profile/api-tokens)
2. Click "Create Token"
3. Use "Edit zone DNS" template or create a custom token with:
   - Permissions: Zone → DNS → Edit
   - Zone Resources: Include → Specific zone → [Your domain]

## How to Build the Plugin

1. Download this repository
2. Ensure you have .NET Core SDK installed
3. Build with

```sh
dotnet publish --configuration Release --output bin
```

4. Place the .dll file in the jellyfin folder called ```plugins/```

## How It Works

By default the plugin runs every 6 hours and:
1. Retrieves your current public IP address
2. Finds the DNS A record for your configured hostname in Cloudflare
3. Updates the record if the IP address has changed

the scheduled task of updating every 6 hours can be modified if you want more frequent or less frequent updates.

