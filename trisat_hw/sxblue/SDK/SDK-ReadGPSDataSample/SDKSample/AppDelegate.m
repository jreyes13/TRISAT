//
//  AppDelegate.m
// SDK Sample
//
//  Created by Sharad Garg on 11/12/14.
//  Copyright (c) 2014 Aman Enterprises Inc. All rights reserved.
//

#import "AppDelegate.h"

#import "GPSManager.h"
#import "NMEAManager.h"

#import "ConnectionManagerRead.h"

#import "ConfigurationTableViewController.h"
#import "DataViewController.h"
#import "LocationViewController.h"
#import "SatellitesView.h"

#include "DLog.h"

typedef NS_ENUM(NSUInteger, tabName) {
    locationTab=0,
    satellitesTab,
    dataTab,
    configurationTab,
    nbrOfTabs
};

@implementation UITabBarController (myApp)

- (BOOL)shouldAutorotate {
    return YES;
}

- (NSUInteger)supportedInterfaceOrientations {
    return (UIInterfaceOrientationMaskPortrait | UIInterfaceOrientationMaskPortraitUpsideDown);
}

@end


@interface AppDelegate () <UITabBarControllerDelegate, ConnectionManagerDelegate> {
    
}

@property (nonatomic, strong) SatellitesView *mySatellites;         // satellites object from lib
@property (nonatomic, weak) UITabBarController *tabBarController;

@end

@implementation AppDelegate

#pragma mark - App life cycle

- (BOOL)application:(UIApplication *)application didFinishLaunchingWithOptions:(NSDictionary *)launchOptions
{
    // Override point for customization after application launch.
    
    // name main thread for debug purpose
    [[NSThread mainThread] setName:@"Main thread"];
    
    // set ourself as GPS cable delegate
    [[ConnectionManager sharedConnectionManager] setDelegate:self];
    
    self.tabBarController = (UITabBarController *)self.window.rootViewController;
    
    // load the satellites view
    self.mySatellites = [[SatellitesView alloc] init];
    UIViewController *mySatellitesVC = [self.mySatellites getSatellitesViewController];
    // configure its item
    UITabBarItem *tab4 = [[UITabBarItem alloc] initWithTitle:@"Satellites" image:[UIImage imageNamed:@"Satellites"] tag:3];
    mySatellitesVC.tabBarItem = tab4;
    // set it as 4th tab bar item
    NSArray *allVC = [self.tabBarController viewControllers];
    NSMutableArray *fullVC = [NSMutableArray arrayWithArray:allVC];
    DLog(@"got %d view controllers", [fullVC count]);
    [fullVC replaceObjectAtIndex:satellitesTab withObject:mySatellitesVC];
    [self.tabBarController setViewControllers:fullVC animated:NO];
    [mySatellitesVC view];      // force view loading
    
    return YES;
}
							
- (void)applicationWillResignActive:(UIApplication *)application
{
    // Sent when the application is about to move from active to inactive state. This can occur for certain types of temporary interruptions (such as an incoming phone call or SMS message) or when the user quits the application and it begins the transition to the background state.
    // Use this method to pause ongoing tasks, disable timers, and throttle down OpenGL ES frame rates. Games should use this method to pause the game.
}

- (void)applicationDidEnterBackground:(UIApplication *)application
{
    // Use this method to release shared resources, save user data, invalidate timers, and store enough application state information to restore your application to its current state in case it is terminated later. 
    // If your application supports background execution, this method is called instead of applicationWillTerminate: when the user quits.
}

- (void)applicationWillEnterForeground:(UIApplication *)application
{
    // Called as part of the transition from the background to the inactive state; here you can undo many of the changes made on entering the background.
}

- (void)applicationDidBecomeActive:(UIApplication *)application
{
    // Restart any tasks that were paused (or not yet started) while the application was inactive. If the application was previously in the background, optionally refresh the user interface.
    DLog(@"enter");
    // get gps info
    if(![CLLocationManager locationServicesEnabled] ||
       ( [CLLocationManager authorizationStatus]!=kCLAuthorizationStatusAuthorized && [CLLocationManager authorizationStatus] != kCLAuthorizationStatusNotDetermined) )
    {
        UIAlertView *alert = [[UIAlertView alloc] initWithTitle:@"Warning"
                                                        message:@"The location service is not allowed for this app. Please enable location service on settings app to enable GPS cable communication."
                                                       delegate:nil
                                              cancelButtonTitle:@"Ok"
                                              otherButtonTitles:nil];
        [alert show];
    }
    else {
        
        NSLog(@"startUpdatingLocation");
        
        // Check for iOS 8. Without this guard the code will crash with "unknown selector" on iOS 7.
        if ([[GPSManager sharedLocationManager]respondsToSelector:@selector(requestAlwaysAuthorization)]) {
            
            NSLog(@"request authorization");
            [[GPSManager sharedLocationManager]requestAlwaysAuthorization];
        }
        
        [[GPSManager sharedLocationManager]startUpdatingLocation];
        [[GPSManager sharedLocationManager]startUpdatingHeading];
    }

}

- (void)applicationWillTerminate:(UIApplication *)application
{
    // Called when the application is about to terminate. Save data if appropriate. See also applicationDidEnterBackground:.
}

#pragma mark - Connection management

// ****** All callbacks are on background thread for performances *****
// Do not block main thread, reserved for UI update

- (void)connectionConnected:(NSString *)protocol {
    DLog(@"enter");
    [self performSelectorOnMainThread:@selector(appDelegate_cableConnected:) withObject:protocol waitUntilDone:NO];
}

- (void)connectionDisconnected {
    DLog(@"enter");
    [self performSelectorOnMainThread:@selector(appDelegate_cableDisconnected) withObject:nil waitUntilDone:NO];
}

- (void)readBytesAvailable:(UInt32)length {
    DLog(@"%i bytes available", (unsigned int)length);
    // wait for at least 64 bytes to get data, for optimisation
    if (length>=128) {
        NSData *updatedData = [[ConnectionManager sharedConnectionManager] getDataFromBytesAvailable];
        
        // update NMEA parser with data from the cable
        NSUInteger res = [[NMEAManager sharedManager] parse:updatedData];
        
        // did we get some nmea sentences?
        if (res>0) {
            // update views with the NMEA info
            [self performSelectorOnMainThread:@selector(appDelegate_updateSatellitesInfo) withObject:nil waitUntilDone:NO];
            
        }
        
    }
}

- (void)didReceivePortConfig {
    DLog(@"enter didReceivePortConfig");
}

- (void)didGpsLoopTest:(BOOL)pass {
    DLog(@"enter with %@", pass==TRUE ? @"TRUE" : @"FALSE");
}

- (void)updateStats {
    DLog(@"enter");
    // update only when view in on configuration tab
    if ([self.tabBarController selectedIndex]==configurationTab) {
        [self performSelectorOnMainThread:@selector(appDelegate_updateStats) withObject:nil waitUntilDone:NO];
    }
}

#pragma mark - UI updates

// main thread UI updates
- (void)appDelegate_cableConnected:(NSString *)protocol {
    DLog(@"enter");
    NSArray *ViewControllersArray = self.tabBarController.viewControllers;
    
    // warn configuration tab
    UINavigationController *myConfNavController = [ViewControllersArray objectAtIndex:configurationTab];
    ConfigurationTableViewController *myConfVC = (ConfigurationTableViewController *)[[myConfNavController viewControllers] objectAtIndex:0];
    [myConfVC connectionConnected:(NSString *)protocol];
    
    // warn data tab
    DataViewController *myData = [ViewControllersArray objectAtIndex:dataTab];
    [myData connectionConnected:(NSString *)protocol];
    
    // warn satellites tab
    [self.mySatellites initSatellitesViews];
}

- (void)appDelegate_cableDisconnected {
    DLog(@"enter");
    NSArray *ViewControllersArray = self.tabBarController.viewControllers;
    
    // warn configuration tab
    UINavigationController *myConfNavController = [ViewControllersArray objectAtIndex:configurationTab];
    ConfigurationTableViewController *myConfVC = (ConfigurationTableViewController *)[[myConfNavController viewControllers] objectAtIndex:0];
    [myConfVC connectionDisconnected];
    
    // warn data tab
    DataViewController *myData = [ViewControllersArray objectAtIndex:dataTab];
    [myData connectionDisconnected];
    
    // warn satellites tab
    [self.mySatellites initSatellitesViews];
    
    // init NMEA parser
    [[NMEAManager sharedManager] initParser];
}

- (void)appDelegate_updateStats {
    DLog(@"enter");
    NSArray *ViewControllersArray = self.tabBarController.viewControllers;
    
    // warn configuration tab
    UINavigationController *myConfNavController = [ViewControllersArray objectAtIndex:configurationTab];
    ConfigurationTableViewController *myConfVC = (ConfigurationTableViewController *)[[myConfNavController viewControllers] objectAtIndex:0];
    [myConfVC updateStats];
}

- (void)appDelegate_updateSatellitesInfo {
    DLog(@"enter");
    NSArray *ViewControllersArray = self.tabBarController.viewControllers;
    nmeaINFO currentInfo = [[NMEAManager sharedManager] getNmeaInfo];
    // warn satellites tab
    [self.mySatellites updateViewWith:currentInfo];
    
    // warn location tab
    LocationViewController *myMapView = [ViewControllersArray objectAtIndex:locationTab];
    [myMapView updateGPSDataWith:currentInfo];
}

- (void)alertView:(UIAlertView *)alertView clickedButtonAtIndex:(NSInteger)buttonIndex {
    DLog(@"enter");
    // exit app after user confirmation
    exit(0);
}

@end
