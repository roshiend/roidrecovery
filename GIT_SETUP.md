# Git Repository Setup Guide

## Push to GitHub (or GitLab/Bitbucket)

### Step 1: Create a New Repository on GitHub
1. Go to https://github.com/new
2. Repository name: `android-file-recovery-tool` (or your preferred name)
3. Description: "C# WPF application for recovering deleted and permanently deleted files from Android devices"
4. Choose **Public** or **Private**
5. **DO NOT** initialize with README, .gitignore, or license (we already have these)
6. Click "Create repository"

### Step 2: Connect Your Local Repository to GitHub

After creating the repository on GitHub, you'll see a page with instructions. Use these commands:

```bash
# Add the remote repository (replace YOUR_USERNAME with your GitHub username)
git remote add origin https://github.com/YOUR_USERNAME/android-file-recovery-tool.git

# Push your code to GitHub
git branch -M main
git push -u origin main
```

### Step 3: Alternative - Using SSH (if you have SSH keys set up)

```bash
git remote add origin git@github.com:YOUR_USERNAME/android-file-recovery-tool.git
git branch -M main
git push -u origin main
```

## Push to GitLab

1. Go to https://gitlab.com/projects/new
2. Create the repository
3. Then run:
```bash
git remote add origin https://gitlab.com/YOUR_USERNAME/android-file-recovery-tool.git
git branch -M main
git push -u origin main
```

## Push to Bitbucket

1. Go to https://bitbucket.org/repo/create
2. Create the repository
3. Then run:
```bash
git remote add origin https://bitbucket.org/YOUR_USERNAME/android-file-recovery-tool.git
git branch -M main
git push -u origin main
```

## Verify the Push

After pushing, refresh your repository page on GitHub/GitLab/Bitbucket to see your files.

## Future Updates

To push future changes:
```bash
git add .
git commit -m "Your commit message"
git push
```

