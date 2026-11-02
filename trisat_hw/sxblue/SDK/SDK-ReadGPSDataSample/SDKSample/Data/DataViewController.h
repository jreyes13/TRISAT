//
//  DataViewController.h
// SDK Sample
//
//  Created by Sharad Garg on 11/12/14.
//  Copyright (c) 2014 Aman Enterprises Inc. All rights reserved.
//

#import <UIKit/UIKit.h>
#include "ConnectionManagerRead.h"

@interface DataViewController : UIViewController <UITextFieldDelegate, ConnectionManagerDelegate>

@property (nonatomic, weak) IBOutlet UITextView  *rxText;
@property (nonatomic, weak) IBOutlet UIButton  *rxInPauseButton;
@property BOOL rxInPauseState;

- (IBAction)pauseStartButtonPressed:(id)sender;

@end
