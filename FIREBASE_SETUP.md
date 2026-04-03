# Firebase Database Setup Guide

## Overview
This application now supports Firebase Realtime Database for invite code management, allowing multiple PCs to share the same invite codes online.

## Setup Instructions

### 1. Create Firebase Project
1. Go to [Firebase Console](https://console.firebase.google.com/)
2. Click "Add project"
3. Enter project name: `PinayPal Backup Manager`
4. Enable Google Analytics (optional)
5. Click "Create project"

### 2. Set Up Realtime Database
1. In your Firebase project, go to "Realtime Database" from the left menu
2. Click "Create Database"
3. Choose a location (recommended: United States)
4. Select "Start in test mode" (allows read/write access)
5. Click "Enable"

### 3. Get Database URL
1. In Realtime Database settings, copy the "Database URL"
2. It should look like: `https://your-project-name-default-rtdb.firebaseio.com/`

### 4. Update Application
1. Open `Services/FirebaseInviteService.cs`
2. Replace the `FirebaseUrl` with your actual database URL:
   ```csharp
   private static readonly string FirebaseUrl = "https://your-project-name-default-rtdb.firebaseio.com/";
   ```

### 5. Set Initial Invite Code
You can set the initial invite code in two ways:

#### Option A: Firebase Console
1. Go to your Realtime Database in Firebase Console
2. Click "Add data"
3. Enter:
   - Name: `inviteCodes`
   - Value: `{"current": "YOUR_INVITE_CODE"}`
4. Click "Save"

#### Option B: Application (Admin)
1. Log in as admin on any PC
2. Go to Settings → Invite Codes
3. Click "Regenerate" to create a new code
4. This will automatically sync to Firebase

## How It Works

### Fallback System
The application uses a smart fallback system:
1. **Firebase (Online)**: First tries to get/set invite codes from Firebase
2. **Config File**: Falls back to local `invite.txt` file
3. **Hardcoded**: Final fallback to hardcoded code `PINAYPAL2024`

### Multi-PC Support
- All PCs with internet access will use the same Firebase invite code
- Offline PCs will use local fallback methods
- Changes to invite codes sync across all connected PCs

### Security
- Firebase is in test mode (read/write enabled)
- For production, consider implementing Firebase Security Rules
- Local database remains encrypted and secure

## Testing

1. Deploy the application to multiple PCs
2. On PC 1 (admin), generate a new invite code
3. On PC 2, use that invite code to register
4. The code should work across all connected PCs

## Troubleshooting

### "Invalid invite code" error
- Check internet connection
- Verify Firebase URL is correct
- Check Firebase database has the invite code structure

### Firebase connection issues
- Ensure Firebase URL is accessible
- Check if Firebase is in test mode
- Verify network connectivity

### Offline mode
- Application will automatically fall back to local methods
- Invite codes may differ between offline PCs
