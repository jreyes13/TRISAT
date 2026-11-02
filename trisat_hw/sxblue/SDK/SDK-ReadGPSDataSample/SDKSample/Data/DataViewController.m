//
//  DataViewController.m
// SDK Sample
//
//  Created by Sharad Garg on 11/12/14.
//  Copyright (c) 2014 Aman Enterprises Inc. All rights reserved.
//

#import "DataViewController.h"

#import "GPSManager.h"
#import "SatellitesView.h"
#import "NMEAManager.h"
#import "DLog.h"

@interface DataViewController () <ConnectionManagerDelegate>

@end

@implementation DataViewController

- (id)initWithNibName:(NSString *)nibNameOrNil bundle:(NSBundle *)nibBundleOrNil
{
    self = [super initWithNibName:nibNameOrNil bundle:nibBundleOrNil];
    if (self) {
        // Custom initialization
    }
    return self;
}

- (void)viewDidLoad
{
    [super viewDidLoad];
    // Do any additional setup after loading the view.
    // prevent edition on rx field
    [self.rxText setEditable:NO];
    
    // register to nmea manager for sentence posts
    [[NSNotificationCenter defaultCenter] addObserver:self selector:@selector(receivedNewNMEASentence:) name:kNMEASentenceNotification object:nil];
}

- (void)viewDidAppear:(BOOL)animated {
    DLog(@"enter");
    // get gps info
    if([CLLocationManager locationServicesEnabled]!= YES ||
       ( [CLLocationManager authorizationStatus]!=kCLAuthorizationStatusAuthorized && [CLLocationManager authorizationStatus] != kCLAuthorizationStatusNotDetermined) )
    {
        UIAlertView *alert = [[UIAlertView alloc]
                              initWithTitle:@"Warning" message:@"The location data is not allowed for this app. Please enable location service on settings app to enable GPS cable communication."
                              delegate:nil
                              cancelButtonTitle:@"Ok"
                              otherButtonTitles:nil];
        [alert show];
    }
}

- (void)didReceiveMemoryWarning
{
    [super didReceiveMemoryWarning];
    // Dispose of any resources that can be recreated.
}

#pragma mark - pause / start button

- (IBAction)pauseStartButtonPressed:(id)sender
{
    if(self.rxInPauseState == NO)
    {
        self.rxInPauseState = YES;
        [self.rxInPauseButton setTitle:@"Start" forState:UIControlStateNormal];
    }
    else
    {
        self.rxInPauseState = NO;
        [self.rxInPauseButton setTitle:@"Pause" forState:UIControlStateNormal];
    }
}

#pragma mark - connection manager delegate

- (void)connectionConnected:(NSString *)protocol {
    DLog(@"enter");
    // init rx view text
    self.rxText.text = @"Cable connected\r\n";
}

- (void)connectionDisconnected {
    DLog(@"enter");
    // clean rx view
    self.rxText.text = @"Cable disconnected\r\n";
}

#pragma mark - update text data

- (void)receivedNewNMEASentence:(NSNotification *)notif {
    DLog(@"enter");
    NSString *nmea = (NSString *)[notif object];
    // remember we are in background thread, update UI on main thread
    [self performSelectorOnMainThread:@selector(updateTextWith:) withObject:nmea waitUntilDone:YES];
}

- (void)updateTextWith:(NSString *)data {
    DLog(@"enter");
    if (self.rxInPauseState==FALSE) {
        // create rx window string
        NSString *currentString = self.rxText.text;
        if (currentString==nil) {
            currentString = @"";
        }
        // create new string
        NSMutableString *nextRxString = [NSMutableString stringWithString:currentString];
        if (data==nil) {
            // nothing to get from data ?
            return;
        }
        
        [nextRxString appendString:data];
        
        //limit to 3000 bytes in the Rx window
        int len = [nextRxString length];
        if(len > 3000)
        {
            NSUInteger curLength = [data length];
            NSRange arange = NSMakeRange(0, curLength);   // delete firsts characters to insert our string
            [nextRxString deleteCharactersInRange:arange];
        }
        
        // change rx string
        self.rxText.text = nextRxString;
        self.rxText.selectedRange = NSMakeRange(self.rxText.text.length, 0);   // still make insertion
    }
}

@end
